# RestaurantSim — Agent Instructions

This file is read automatically by Claude Code when working in this
repository. It captures locked project conventions so they don't need to be
re-explained every session. Nothing here overrides the design docs below —
this is the "how to work in this codebase" layer, not game design.

**Reference docs (read these for design context, not duplicated here):**
- `restaurant-simulator-business-logic.md` — customer lifecycle, staff,
  economy, events, DLC scope
- `restaurant-simulator-entities.md` — full locked data model (24 entities)
  (24 numbered entities plus preset-data/config and derived-stage types — see
  its table of contents)
- `restaurant-simulator-mechanics.md` — concrete formulas: Mood, complaints,
  tips, kitchen scheduling, pathing, service flow, day boundaries, table
  merge/movement
- `restaurant-simulator-presets.md` — the MVP `default_diner` preset content
- `restaurant-simulator-status.md` — **single source of truth for build
  status, open decisions and verification items** (check it before assuming
  anything is built or decided)
- `restaurant-simulator-architecture.md` — solution structure, runtime
  model, communication strategy, platform ambitions

**Project status:** design phase complete; implementation is underway. Per
the implementation-phase log, the command-driven core loop (architecture doc
§10 Phase 4), tick-driven systems (Phase 5) and AutoManager (Phase 6) are
built — only the optional `IKitchenSyncStrategy` extraction remains in
Phase 6. Phase 7 (Blazor client over SignalR) is underway — see architecture doc §10; Phase 8 is not recorded here. Do not assume any folder
structure, file, or tooling beyond what's specified below — ask before
assuming anything not already locked in the docs above.

---

## Core Principles (apply to everything you write here)

1. **Correctness over speed.** Move deliberately. Question assumptions
   rather than defaulting to the familiar. Flag contradictions or gaps
   against the design docs instead of silently smoothing over them.
2. **Data-driven, preset-agnostic.** No hardcoded restaurant configurations
   — presets are JSON data, not code.
3. **No god objects.** Distribute state/logic into cohesive sub-groups, not
   flat aggregates (see `Restaurant`'s `Floor`/`StaffRoster`/etc. split as
   the reference example).
4. **Extensibility by design.** DLC modules and AutoManager strategies must
   slot in without modifying core entities or handlers.

---

## Solution Structure

Separate `.csproj` per layer, referencing **inward only**:

```
RestaurantSim.Domain          (no project references — pure logic, entities, rules)
RestaurantSim.Contracts       (no references — wire DTOs shared with the client)
RestaurantSim.Application     → Domain, Contracts
RestaurantSim.Modules.*       → Domain, Application (public contracts only)
RestaurantSim.AutoManager     → Domain, Application (public contracts only)
RestaurantSim.Infrastructure  → Application, Domain, Contracts
RestaurantSim.Api             → all of the above (composition root)
RestaurantSim.Client          → Contracts (Blazor WASM; SignalR only)
RestaurantSim.Client.Tests    → Client
```

- **Domain** must never reference Infrastructure, Api, or any specific
  transport/persistence concern. No `HttpContext`, no SignalR types, no
  EF Core types — pure logic and entities only.
- **Modules.\*** and **AutoManager** reference only the *public* contracts
  of Domain/Application (interfaces, commands, events) — never internals.
  If a module or AutoManager strategy seems to need something beyond the
  public command/event surface, that's a signal the surface itself is
  incomplete — flag it, don't work around it.
- **Api** is the only project allowed to know about every other project.
  All DI wiring, module registration, and AutoManager strategy
  registration happens in `Api`'s composition root. `AddMediatR` must scan **both** the Application assembly
  (handlers) and the Infrastructure assembly (SignalR notification handlers)
  — it originally scanned Application only, so no event was ever pushed to
  clients (found only on the first live run).
- **Why `Application → Contracts`:** the snapshot query must map to DTOs
  *inside* the per-restaurant lock — mapping after release would be a torn
  read against the tick loop. `Contracts` is pure data with no dependencies,
  so no cycle is possible. Rejected alternative: an Application-owned read
  model mapped again in Infrastructure (duplicated shapes, no benefit).
  `Infrastructure` also references `Contracts`: hub methods and event push
  send/receive Contracts types, never Domain types.
- Test projects mirror this split 1:1 (`RestaurantSim.Domain.Tests`, etc.)
  — see Testing below.

---

## Runtime Model (context, not something you'll implement early)

- Fixed-tick simulation via a `BackgroundService`; `SimulationTick`
  advances once per real-world interval, derived from
  `RestaurantConfig.TicksPerRealSecond` (mechanics doc §14).
  `RestaurantConfig.GameMinutesPerRealSecond` (`double`, authored;
  `default_diner` = 1) sets game pace. `MinutesPerTick` is no longer stored —
  it is a derived property, `GameMinutesPerRealSecond / TicksPerRealSecond`
  (can be fractional). The two authored knobs stay separate: pace vs. tick
  resolution — changing `TicksPerRealSecond` does not change perceived pace
  (architecture doc §2, mechanics doc §14).
  `SimulationTickService` paces each restaurant by its own
  `TicksPerRealSecond` via `TickPacer`: the loop wakes every ~10 ms and ticks
  each restaurant that is due (practical ceiling ≈ 60 ticks/s from OS timer
  resolution); an overloaded host drops the backlog and slows down rather
  than bursting ticks. Durations are authored in seconds and per-tick rates
  *per real second* — nothing is authored in ticks (mechanics doc §14).
- Client communication is **SignalR-only** for MVP — no REST layer.
  One-shot requests (initial state, preset lists) are hub method
  invocations on the existing connection, not separate HTTP calls. Hub methods return `HubResult<T>` (Contracts) — the wire
  form of `Result<T>`; business failures travel as data, not `HubException`.
- Push strategy is **event-based**: Domain raises MediatR notifications
  (`GroupSeated`, `OrderItemReady`, etc.); an Infrastructure handler
  translates relevant events into SignalR messages. Never broadcast full
  aggregate state per tick. Events that change Cash or Reputation carry the
  **absolute** value after the change, never a delta, and every pushed DTO
  carries `Tick` (architecture doc §3).
- All aggregate reads/writes go through a repository interface
  (`IRestaurantRepository`), even though MVP's implementation is
  in-memory only. Never bypass this to touch storage directly.

---

## Concurrency (see architecture doc §11 for the full model)

- **Every command, the `SimulationTick` itself, and every AutoManager
  decision go through a per-`RestaurantId` async lock** — a
  `SemaphoreSlim(1,1)` per restaurant, held in a
  `ConcurrentDictionary<Guid, SemaphoreSlim>`. Implemented as a MediatR
  pipeline behavior (`RestaurantLockBehavior<TRequest, TResponse>`)
  ordered **ahead of** `ValidationBehavior`. Nothing bypasses it — not a
  "fast path," not an internal side-effect, not AutoManager. Nested handlers a tick command calls directly run *inside*
  the already-held lock (see the nested-handlers bullet below) — not a bypass;
  the one real, documented exception is the tick increment (known-deviation
  bullet below).
- **Queries that read restaurant state go through the lock too.** They
  implement `IRestaurantScopedRequest` and pass through
  `RestaurantLockBehavior` (e.g. `GetRestaurantSnapshotQuery`), mapping to
  Contracts DTOs *inside* the lock.
- **Nested handlers pattern.** A tick command that must act through other
  commands (`AdvanceDiningCommand` → PlaceOrder / DeliverOrderItem / PayBill /
  LeaveTable) calls their handlers **directly** while holding the lock, never
  via `ISender`: `RestaurantLockBehavior`'s `SemaphoreSlim(1,1)` is not
  re-entrant, so a nested send deadlocks. Safe because those handlers
  re-validate their own preconditions; the only thing bypassed is the
  FluentValidation behavior. (AutoManager keeps its own pattern: separate
  sequential pass, lock per decision.)
- **Events publish after save.** `AdvanceDiningCommandHandler` takes
  `IPublisher` and publishes its stage events after its single `SaveAsync`
  (inside the lock, like every event), so a listener never sees unsaved state.
  Nested handlers still publish their own events (`OrderPlaced`,
  `OrderItemServed`, `BillPaid`, `GroupLeft`) during the pass, before the save
  — pre-existing behaviour, unchanged.
- **Known deviation (deferred):** `RestaurantTickProcessor` increments
  `SimulationTick` and updates `DailyStats` *outside* the lock, contrary to the
  first bullet above. Proposed fix: an `AdvanceTickCommand`
  (`IRestaurantScopedRequest`) for that first block. Deferred — nothing mutates
  a restaurant concurrently yet (no player commands; the snapshot query is the
  only reader). Revisit when manual commands exist.
- **AutoManager does not run inside the tick.** It runs as its own
  sequential pass immediately after a tick finishes and releases the
  lock, acquiring/releasing the lock **per individual decision**, not
  once for the whole pass — a waiting player command's worst-case delay
  is one decision's processing time, not a full pass.
- **Validators must always re-check current state at handle-time, never
  trust a pre-fetched/stale read.** The lock prevents concurrent
  corruption, but two sequential commands can still legitimately change
  what's true between when a caller decided to act and when its command
  actually runs (e.g. two seat requests for the same table moments apart
  — the second must see the table is no longer free). Never assume a
  state check done before dispatch is still valid by handle-time.
- **"Check all, then commit all"** — named convention, applies anywhere a
  command reserves more than one resource, or overrides an in-progress
  task/reservation. Validate every precondition first; only commit
  changes once everything checks out. An override that turns out invalid
  (no valid path, no available cook, etc.) must leave the *old*
  reservation/task untouched, not abandon it into limbo. Applies to:
  kitchen station+cook reservation, movement/task overrides, staff
  reassignment. Any new command matching this shape follows the same
  rule by default.
- **No inline side-effects that mutate restaurant state outside a
  dispatched command.** If a behavior is described in a design doc as
  happening "automatically" off some trigger (e.g. section rebalancing
  when staff count changes), it must be a real command
  (`RecomputeSectionAssignmentsCommand`, etc.) going through the normal
  pipeline — an inline side-effect bypasses the lock above and is a bug,
  not a shortcut.

---

## Naming Conventions

- **Commands:** `VerbNounCommand` (e.g. `SeatGroupCommand`,
  `PlaceOrderCommand`)
- **Queries:** `GetNounQuery` / `NounByXQuery`
- **Handlers:** `{Command/Query}Handler` — one handler per file
- **Domain events:** past-tense fact (`GroupSeated`, `MoodChanged`,
  `OrderItemReady`)
- **Interfaces:** `I` prefix (`ISeatingStrategy`, `IRestaurantRepository`)
- **Async methods:** `Async` suffix (`GetAsync`, `SaveAsync`) — standard
  .NET convention, no exceptions
- **Fields (enforced by `.editorconfig`):** private instance fields
  `_camelCase`; **constants and `static readonly` fields PascalCase** (the
  original rule demanded `_` on those too — fixed via the exemption rules).
  MediatR's `Handle(...)` is async without the `Async` suffix by necessity;
  exempt it in `.editorconfig` only if Visual Studio flags it.

---

## Nullable Reference Types

- `<Nullable>enable</Nullable>` in every `.csproj`. Nullable warnings are
  build errors, not just warnings.
- Nullability must match the entity doc's intent exactly: a field
  documented as `Type?` (e.g. `ClusterId: int?`) means "legitimately
  absent sometimes" — a field documented as `Type` means "always present,
  treat null as a bug, not a state to handle."
- Never suppress a nullable warning with `!` (null-forgiving operator) as
  a shortcut — if the compiler thinks something might be null, either
  prove it can't be (guard clause) or the field's nullability in the
  entity doc needs revisiting. Flag the latter rather than silently
  forcing it.

---

## Validation

- **FluentValidation**, run as a MediatR pipeline behavior
  (`ValidationBehavior<TRequest, TResponse>`) ahead of every command
  handler.
- Validators live alongside the command they validate, not inside the
  handler. Handlers assume the request is already valid by the time they
  run. (Exception: handlers a tick command calls directly — the
  nested-handlers pattern — skip the validation pipeline, so they re-validate
  their own preconditions.)
- Business-rule gates described in the entity doc as "command validation"
  (e.g. kids-menu enforcement on `OrderItem`, HeadChef singleton check)
  belong here, not scattered inline in handler logic.
- Validators run at handle-time, inside the per-restaurant lock — see
  Concurrency above. Never validate against a state snapshot taken before
  the command was dispatched.
- **No duplicated validator/handler logic.** Any business-rule check used by
  both a command's validator and its handler (e.g. "is this table free and
  does the group fit") must live in exactly one shared method — Domain if
  it is pure logic with no I/O (e.g. `TableCluster.CanSeat`,
  `OrderPhaseTransition.IsValid`), Application if it needs repository
  access (e.g. `PlaceOrderItemValidation.AllItemsAreValidAsync`). Both
  layers call that same method; never independently re-implement the check
  in each place. This keeps the "validator re-checks at handle-time" safety
  guarantee (architecture doc §11) and the handler's own defense against
  being invoked directly (e.g. from a test bypassing the pipeline), without
  two copies that can drift apart when only one gets edited. (Found in
  `SeatGroupCommand` and `PlaceOrderCommand`, which originally each
  re-implemented the same check twice.)
- **Shared menu-eligibility rule.** `MenuItemEligibility.IsOrderable` (Domain,
  pure: course match, kids-menu rule, meal-period availability) is the one
  place for those checks; `PlaceOrderItemValidation` and `OrderGenerator` both
  call it — same pattern as `TableCluster.CanSeat` /
  `OrderPhaseTransition.IsValid` above.

---

## Error Handling

- **Expected business-rule failures → `Result` / `Result<T>` return
  values**, not exceptions. Examples: table unavailable, kids-menu
  violation, queue full. These are normal gameplay outcomes the caller
  (UI, AutoManager) needs to react to, not error conditions.
- **Exceptions → reserved for actual bugs/invariant violations.**
  Something that should be structurally impossible if the code is
  correct (e.g. a required aggregate reference is null). Do not use
  exceptions for anything a player action can legitimately trigger.
- If you're unsure which bucket a failure belongs to, default to
  `Result` — it's the cheaper, more explicit choice, and matches the
  "correctness over speed" principle (the failure modes are visible in
  the method signature, not hidden in a try/catch someone has to know to
  write).

---

## Testing

- **Framework:** xUnit. **Mocking:** NSubstitute. **Assertions:**
  FluentAssertions.
- One test project per source project (`RestaurantSim.Domain.Tests`,
  `.Application.Tests`, `.AutoManager.Tests`, etc.) — never one combined
  test project.
- **No fixed coverage percentage target.** Coverage priority is
  qualitative:
  - **Domain — highest priority.** This is where the carefully-specified
    rules from the design docs (Mood/Instability/Patience interactions,
    complaint severity, heat-lamp-vs-reinform decision, section
    balancing) actually get locked in against regression. Test these
    thoroughly.
  - **Application (handlers)** — test against a mocked repository:
    correct state changes, correct validation rejections.
  - **AutoManager** — test strategies in isolation (given inputs X, does
    the strategy pick the expected output).
  - **Infrastructure/Api** — lightest coverage; mostly wiring. Don't
    manufacture tests just to hit a number.
- When implementing a new command/handler, write its test in the same
  work session — don't defer it to "later."

---

## Frontend

- **Blazor WASM**, structured to remain MAUI-Hybrid-compatible (avoid any
  Blazor-Server-only API or assumption of a live server connection for
  correctness).
- Frontend code talks to the engine exclusively through `IEngineClient`
  (today: SignalR to a server-hosted engine) — no direct access to
  Domain/Application from the client; `Client` references only `Contracts`.
  UI code must not assume SignalR, a remote server, or an in-process engine.
- Client state (`RestaurantStore`) is pure, unit-tested logic: it drops events
  with `Tick < snapshot.SimulationTick`, applies equal-tick events
  idempotently (set state, never toggle), sets a group's dining stage from
  `GroupDiningStageChanged` (never infers it; `GroupSeated` starts a group as
  `Ordering`/`Drinks`), and ignores events for a group it never saw seated (no
  `PartySize` to invent it with). `EngineSession` (depends only on
  `IEngineClient`) rejoins and replaces the store with a fresh snapshot after a
  reconnect (architecture doc §3).
- **Offline/MAUI (intended, not built):** an in-process `IEngineClient`
  implementation would live in a separate host project that references
  Application/Infrastructure; `Client` itself stays Contracts-only
  (architecture doc §6.2).

---

## Git Conventions

- **Commit prefixes:** `feat:`, `fix:`, `refactor:`, `test:`, `docs:` —
  conventional-commit-ish. Apply from the very first commit, not
  retrofitted later.
- **Branching:** trunk-based, short-lived feature branches
  (`feature/seat-group-command`), merged back promptly. No long-running
  branches.

> **Reminder for the first real coding session:** this section exists
> now, before any code does, specifically so it's not forgotten once
> implementation starts. Apply it to the very first commit.

---

## Preset Data

- All presets (`RestaurantConfig`, `TableShape`, `MenuItem`,
  `KitchenStationType`, etc.) load from JSON files on disk via
  `IPresetRepository<T>`. Never hardcode a preset value in code — if a
  value varies per restaurant/config, it belongs in JSON, not a constant.
- Actual MVP preset content (the `"default_diner"` config, table layout,
  staff roster seed, and full menu) is locked in
  `restaurant-simulator-presets.md`. Convert directly to JSON when
  scaffolding the `Api/Presets/` directory — don't re-derive values.
- **Implementation note:** the first-pass layouts currently ship as C# seeds
  (`DefaultDinerLayoutSeed`, `DefaultDinerKitchenLayoutSeed`); the
  JSON-on-disk conversion above is still pending.
- `RestaurantConfig.PreOpeningLeadMinutes` (nullable, code default 30) — see
  presets doc. `RestaurantConfig.ServiceFlow` is a **nested record**
  (`ServiceFlowConfig`: timings in seconds, ready/refill/dessert chances,
  price bias, eating-duration ranges) rather than ~20 flat fields (principle
  3); a preset that omits it gets the defaults.

---

## Folder / File Layout (locked)

Mirrors the entity doc's sub-state groupings (`Floor`/`StaffRoster`/
`CustomerService`/`KitchenOps`/`Events`) — one taxonomy reused everywhere
instead of inventing a second one for the file tree.

**`RestaurantSim.Domain/`**
```
Floor/           — IPlaceable, TableShape, Table, Seat, TableCluster,
                   KitchenStation, KitchenStationType, HeatLamp, Bathroom,
                   Freezer
Staff/           — StaffMember, StaffStats
CustomerService/ — CustomerGroup, Customer, CustomerCall, Reservation,
                   DiningStage, DiningStageCalculator (derived dining stage —
                   pure; also `Calculate(Restaurant, CustomerGroup)`, which
                   resolves the current order, shared by the snapshot
                   mapper and AdvanceDining's change detection)
KitchenOps/      — Order, OrderItem, KitchenTicket, MenuItem
Events/          — RestaurantEvent, SpecialRole tagging pattern,
                   ComplaintRecord (DLC-only — populated solely by
                   ComplaintsBookModule, entity doc §11; the type lives
                   in Domain because Restaurant.Events.ComplaintLog needs
                   a typed slot and Domain can't reference Modules),
                   DurationBasedResolution, CustomerGroupBoundResolution
                   (built-in IEventResolutionStrategy implementations)
Statistics/      — StatisticsSnapshot
Scheduling/      — MealPeriodDefinition (preset data, string-keyed
                   MealPeriodId — same pattern as TableShape/
                   KitchenStationType, added coherence-check pass — cross-cuts CustomerService's
                   Arrival gating and Staff/'s shift boundary, doesn't
                   belong under either alone)
Abstractions/    — IRestaurantRepository, IPresetRepository<T>,
                   ISimulationModule, ISnapshotStore,
                   IEventResolutionStrategy
DomainEvents/    — GroupSeated, OrderItemReady, MoodChanged, etc.
Restaurant.cs        — aggregate root
RestaurantConfig.cs  — preset root
```
- `IRestaurantRepository` lives in **Domain**, not Application — it's
  the aggregate's own persistence contract; Application orchestrates use
  cases, Infrastructure implements the contract.

**Named Domain helpers** (pure; exact folders to be confirmed against the repo
and then added to the map above — status doc, V-07): `TickRates`
(`SecondsToTicks`, `PerTick`), `MealPeriodCalculator` (`InitialTick`,
`FindServicePeriod`), `MenuItemEligibility`, `OrderGenerator`,
`ServiceFlowConfig` / `ServiceFlowTimings`, `TableMergeRules`, `MergeGeometry`,
`TableMoveTiming`, `Footprint`, `WaiterWorkload`, `FloorQueries`,
`MoodTierCalculator`, `PerformanceMultipliers`, `KitchenPrepTimeCalculator`.

**`RestaurantSim.Application/`** — vertical-slice-per-command:
```
Commands/
  SeatGroup/    — SeatGroupCommand, Handler, Validator
  PlaceOrder/   — same pattern
  ...           — one folder per command
Queries/
  GetRestaurantSnapshot/ — query, handler, mapper (IRestaurantScopedRequest;
                   maps to Contracts DTOs inside the lock)
  ...           — one folder per query, same pattern
Common/         — Result<T>, ValidationBehavior (MediatR pipeline),
                   RestaurantLockBehavior (MediatR pipeline, ordered
                   ahead of ValidationBehavior — see Concurrency above)
```

**`RestaurantSim.Infrastructure/`**
```
Persistence/         — InMemoryRestaurantRepository
Presets/              — JsonPresetRepository<T>
SignalR/               — RestaurantHub, one `*NotificationHandler` per
                         pushed event (domain event → Contracts DTO → SignalR),
                         incl. GroupDiningStageChanged / ClustersMerged /
                         ClusterSplit handlers
BackgroundServices/     — SimulationTickService (per-restaurant pacing via `TickPacer`)
```

**`RestaurantSim.Contracts/`** — wire DTOs, no references:
```
Snapshot/  — snapshot DTOs (`ActiveGroupDto` carries Stage/NextVisit),
             DiningStageNames / DiningVisitNames / TurnedAwayReasonNames
Events/    — one DTO per pushed event (every one carries Tick)
Hubs/      — HubResult<T>, RestaurantHubMethods, ClientEventNames
```
Wire-name classes hold strings because Contracts has no Domain reference;
`DiningNamesTests` / `TopologyEventHandlersTests` pin them to the Domain enum
member names — any new `DiningStage`/`DiningVisit`/turned-away-reason member
must be added to both, or the test fails.

**`RestaurantSim.Client/`** — Blazor WASM, SignalR only:
```
Engine/      — IEngineClient, SignalREngineClient
State/       — RestaurantStore, EngineSession
Components/  — FloorView, DiningLabels (all stage wording lives here)
Pages/
Time/        — GameClock
```

**`RestaurantSim.Api/`** — thin: `Program.cs` (composition root),
`appsettings.json`, `Presets/*.json` (content, copied to output on
build). No business logic here, ever.

**`RestaurantSim.Modules.<Name>/`** (e.g. `Modules.FoodCritic`)
```
<Name>Module.cs   — ISimulationModule implementation
EventHandlers/     — subscribes to relevant domain events
```
A module introducing a new `RestaurantEvent` type also supplies its own
`IEventResolutionStrategy` implementation here (architecture doc §5.5) —
registered against its `EventType` in `Api`'s composition root alongside
the module itself, same as everything else module-owned.

**`RestaurantSim.AutoManager/`**
```
Strategies/
  Seating/       — ISeatingStrategy + default implementation
  WaiterAction/   — IWaiterActionStrategy + default
  KitchenSync/    — IKitchenSyncStrategy + default
  TaskPriority/   — IStaffTaskPriorityStrategy + default (Busser
                    table-vs-bathroom ordering — mechanics doc §13)
  TableLayout/    — ITableLayoutStrategy + DefaultTableLayoutStrategy (runtime
                    table merge/split — mechanics doc §18). Consulted BEFORE
                    ISeatingStrategy on every seating step; follows the existing
                    Seating toggle (no new AutoManagerSystem member)
```

**Layering note — `IAutoManagerRunner` (implementation-phase backport):**
strategy interfaces live in the `RestaurantSim.AutoManager` project (above),
not `Domain/Abstractions` like every other abstraction. That created a real
layering question — `SimulationTickService` (Infrastructure) can't reference
`AutoManager` (`Infrastructure → Application, Domain` only). Resolved with
`IAutoManagerRunner`, **defined in Application**, implemented by
`AutoManagerProcessor` in `AutoManager`, and wired through DI at `Api`'s
composition root. `RestaurantTickProcessor` (Application) calls the
interface, never the concrete `AutoManager` project. `AutoManagerProcessor`
runs after every tick (after the lock is released — architecture doc §11)
and takes the lock per individual decision.

**Known gap — `KitchenSync/`:** `IKitchenSyncStrategy` is not yet extracted.
MES v1's decision logic currently lives inline in
`AdvanceKitchenCommandHandler`. Functionally correct (MES is never
toggleable), but doesn't yet match mechanics doc §10's stated intent that it
sit behind a strategy interface for a future MES v2 swap. Pure refactor, no
behavior change, not urgent.

**Tests** — each `.Tests` project mirrors its source project's folder
names exactly (e.g. `Domain.Tests/CustomerService/CustomerGroupTests.cs`)
— a file's test is always one predictable path away. `Client.Tests` mirrors `Client` (`State/`, `Time/`). `Application.Tests/Support/` holds shared builders (`TestRestaurantConfig`, `TestFloors.Row`).

---

## Commands Introduced This Session (locked names; build status noted per command)

Named here because they were identified as necessary during the
concurrency sweep but don't appear in any command list elsewhere yet.
Follow the same `VerbNounCommand` convention and `Commands/<Name>/` slice
pattern as everything else — no special treatment.

- `AcknowledgeCallCommand` — waiter picks up a `CustomerCall` (entity doc
  §10)
- `RecomputeSectionAssignmentsCommand` — replaces the implicit
  `SectionAssignmentService` side-effect; must be dispatched, not inlined
  (see Concurrency above, entity doc §5)
- `BussersCleanBathroomCommand` — Busser cleaning task dispatch (mechanics
  doc §13)
- `LeaveTableForBathroomCommand` / `ReturnToTableCommand` — **finalized,
  coherence-check pass** (previously unnamed placeholder). Sets
  `Customer.IsPresentAtTable` (mechanics doc §12); `ReturnToTableCommand`
  also applies the cleanliness-driven Mood delta on return
- `DeliverOrderItemCommand` — plating/delivery leg dispatch (mechanics doc §10).
  **Built** (implementation-phase backport), including pickup-item handling
  and `QualityAtServe` computed at pickup. Still *not* built:
  `LeaveTableForBathroomCommand` / `ReturnToTableCommand` above (the
  serve-time bonus for absent customers waits on them — mechanics doc §10)

**Added — coherence-check pass, day/season session:**
- `EndDayCommand` — sim-loop-dispatched (not player-facing) once
  `DayEndEligible` (mechanics doc §15); appends the day's
  `StatisticsSnapshot`, applies day-end costs, resets bathroom
  cleanliness, advances to the next day. **Built, except day-end costs**
  (no cost formula exists yet; `Costs` is always 0 — mechanics doc §15)
- `SkipToNextMealPeriodCommand` — player-facing, optional convenience for
  jumping an intra-day gap between two active meal periods (mechanics doc
  §15)
- `PrioritizeTicketCommand` — player-facing, toggles
  `KitchenTicket.PlayerPrioritized` (mechanics doc §10)

**Added — coherence-check pass, `DomainEvents/` additions:**
- `DayEnded` — past-tense fact raised by `EndDayCommand` (mechanics doc
  §15)

**Added — implementation-phase backport (commands built, names now locked):**
- `CreateRestaurantCommand` — session bootstrap: converts
  `RestaurantConfig.StaffRosterSeed`/`TableLayoutId` into a real staffed,
  furnished `Restaurant`. Seeds `FloorState.Freezers` (entity doc §18a/§19).
  Seeded staff start `State = Idle` — a flagged simplification, see entity
  doc §24a
- `PayBillCommand`, `LeaveTableCommand` — complete the Phase 4 loop.
  `LeaveTableCommand` requires `LifecycleState == Leaving` (set only by
  `PayBillCommand`): it is an already-paid group's departure, **not** a
  mid-meal walkout (a distinct, unbuilt mechanic)
- `CleanTableCommand` — **name was never previously locked** (only
  `BussersCleanBathroomCommand` was); `AdvanceCleaningProgressCommand` —
  tick-driven cleaning progress, same pattern as `AdvanceKitchenCommand`
- `TellJokeCommand` — joke resolution (mechanics doc §11)
- Tick-dispatched, in the architecture doc §2a order: `AdvanceKitchenCommand`
  and `SpawnArrivalCommand` (step 1), `AdvanceMoodCommand` and
  `AdvanceStaffShiftCommand` (step 2), `SpreadGroupMoodCommand` (step 3),
  `ProcessQueueAbandonmentCommand` (step 4), `EndDayCommand` (step 5).
  *(The implementation log writes `SpawnArrival`/`ProcessQueueAbandonment`
  without the suffix — class names here follow the `VerbNounCommand`
  convention; confirm against the code.)*

**Domain-event notes (implementation-phase backport):**
- `GroupLeft` carries `PartySize` (added — without it, statistics would
  count table-visits instead of people); `BillPaid` carries the computed tip
- No per-tick domain event fires for Mood (architecture doc §3).
  `MoodChanged` is reserved for a future, targeted use — firing only on Mood
  *tier* boundary crossings — and is not built yet
- `ISnapshotStore.AppendAsync`/`QueryAsync` take `Guid restaurantId` as an
  explicit parameter instead of a field on `StatisticsSnapshot`, keeping
  entity doc §23's struct unchanged (architecture doc §7.5)

---

## Commands / Events Added — Client-Contract, Dining Flow, Merge/Split & Table Movement (implementation backport)

**Commands / queries (locked names):**
- `GetRestaurantSnapshotQuery` — `IRestaurantScopedRequest`, runs under the lock,
  maps to Contracts DTOs inside it.
- `AdvanceDiningCommand` — tick-dispatched; drives waiter visits, order
  generation/placement, delivery, payment and leaving by calling the
  PlaceOrder / DeliverOrderItem / PayBill / LeaveTable **handlers directly**
  (see Concurrency — nested handlers pattern). Architecture §2a step 1:
  `AdvanceTableMovesCommand` → `AdvanceKitchenCommand` →
  `AdvanceDiningCommand` → `SpawnArrivalCommand`; step 2 (unchanged) includes
  `AdvanceCleaningProgressCommand`. **Verify against `RestaurantTickProcessor`**
  — the implementation notes listed cleaning progress with step 1 (status doc,
  V-01).
- `MergeClustersCommand`, `SplitClusterCommand` — runtime table merge/split by
  sliding tables (mechanics doc §18). A split requires the cluster Free (so not
  Moving) and is atomic ("check all, then commit all"): a blocked home cell →
  `Result.Failure`, nothing moves.
- `AdvanceTableMovesCommand` — tick step 1, **first**: tables whose
  `MoveEndTick <= tick` complete (`Table.CompleteMove`); one `TableMoveCompleted`
  per cluster, published after the save.
- `SeatGroupCommand` now also schedules the group's first waiter visit
  (`SeatedToFirstVisitSeconds` after seating), so it needs `RestaurantConfig`.
- `SpawnArrivalCommand` also turns away oversize parties (`PartyTooLarge`,
  mechanics doc §18).

**Domain-event notes:**
- `GroupArrived` now carries `PartySize` (a client can show the queued group
  without a refetch — same reasoning as the `GroupLeft` backport).
- State-changing events carry the **absolute** resulting value:
  `BillPaid.CashAfter`, `GroupAbandonedQueue.ReputationAfter`,
  `GroupTurnedAway.ReputationAfter` (the three places Cash/Reputation change).
- `GroupTurnedAway` gained `Reason` (`QueueFull` | `PartyTooLarge`, default
  `QueueFull`).
- `GroupDiningStageChanged(RestaurantId, GroupId, Stage, NextVisit, Tick)` —
  absolute resulting stage, raised by `AdvanceDiningCommandHandler` at most once
  per group per tick, only on change (mechanics doc §17).
- `ClustersMerged` / `ClusterSplit` carry `Moves` (`TableMove`: TableId, From,
  To) and `DurationTicks`; new `TableMoveCompleted(RestaurantId, ClusterId,
  Tick)`.
- Decision recorded: dining progress is pushed as one server-computed stage per
  group, not as raw `OrderPlaced`/`OrderItemServed` events the client would
  have to interpret — those stay unpushed, and `ConsumedByTick` never leaves
  the server.

---

## What's Still Open (do not assume, ask)

- Save/load, accounts, cloud sync — explicitly deferred until after MVP
  + all DLC modules are complete. Do not build toward these prematurely.
- REST API layer — deferred until an actual external consumer needs
  plain HTTP access. Do not add REST endpoints "just in case."
- **Fatigue unit** (12 real vs in-game hours, mechanics doc §13a) — flagged,
  undecided.
- **Restaurants are never ended or evicted** — any client can create unlimited
  restaurants through the hub, each ticking forever. Needs a decision before the
  engine is exposed publicly.
- **MediatR 13+** logs a commercial-license notice without a key — fine in
  development; needs a license or a pin to the last free line before any public
  release.
- **Android:** client-sim vs server-hosted engine (the `IEngineClient`
  abstraction keeps it open).
- **Tick-increment lock deviation** — see Concurrency (deferred).
- **Everything else open or unverified:** see `restaurant-simulator-status.md`.
- **Cleanup, not urgent:** seconds → ticks conversion now exists generically as
  `TickRates.SecondsToTicks`; older call sites still use
  `KitchenPrepTimeCalculator.PrepTicks` and similar (same formula, mechanics
  doc §14).
