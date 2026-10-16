# RestaurantSim — Agent Instructions

This file is read automatically by Claude Code when working in this
repository. It captures locked project conventions so they don't need to be
re-explained every session. Nothing here overrides the design docs below —
this is the "how to work in this codebase" layer, not game design.

**Reference docs (read these for design context, not duplicated here):**
- `restaurant-simulator-business-logic.md` — customer lifecycle, staff,
  economy, events, DLC scope
- `restaurant-simulator-entities.md` — full locked data model (24 entities)
- `restaurant-simulator-architecture.md` — solution structure, runtime
  model, communication strategy, platform ambitions

**Project status:** design phase complete; implementation is underway. Per
the implementation-phase log, the command-driven core loop (architecture doc
§10 Phase 4), tick-driven systems (Phase 5) and AutoManager (Phase 6) are
built — only the optional `IKitchenSyncStrategy` extraction remains in
Phase 6. Phases 7–8 are not recorded here. Do not assume any folder
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
RestaurantSim.Domain          (no references — pure logic, entities, rules)
RestaurantSim.Application     → Domain, Contracts
RestaurantSim.Contracts       (no references — wire DTOs shared with the client)
RestaurantSim.Modules.*       → Domain, Application (public contracts only)
RestaurantSim.AutoManager     → Domain, Application (public contracts only)
RestaurantSim.Infrastructure  → Application, Domain
RestaurantSim.Api             → all of the above (composition root)
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
  registration happens in `Api`'s composition root.
- Test projects mirror this split 1:1 (`RestaurantSim.Domain.Tests`, etc.)
  — see Testing below.

---

## Runtime Model (context, not something you'll implement early)

- Fixed-tick simulation via a `BackgroundService`; `SimulationTick`
  advances once per real-world interval, derived from
  `RestaurantConfig.TicksPerRealSecond` (mechanics doc §14).
  `RestaurantConfig.MinutesPerTick` controls sim-time-per-tick
  independently of the real interval — these are two separate knobs,
  don't conflate them (see architecture doc §2 for the distinction;
  `MinutesPerTick` itself is only discussed there, not in mechanics.md).
- Client communication is **SignalR-only** for MVP — no REST layer.
  One-shot requests (initial state, preset lists) are hub method
  invocations on the existing connection, not separate HTTP calls.
- Push strategy is **event-based**: Domain raises MediatR notifications
  (`GroupSeated`, `OrderItemReady`, etc.); an Infrastructure handler
  translates relevant events into SignalR messages. Never broadcast full
  aggregate state per tick.
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
  "fast path," not an internal side-effect, not AutoManager.
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
  run.
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
- Frontend code talks to the engine exclusively over SignalR — no direct
  access to Domain/Application from the client.

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
CustomerService/ — CustomerGroup, Customer, CustomerCall, Reservation
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

**`RestaurantSim.Application/`** — vertical-slice-per-command:
```
Commands/
  SeatGroup/    — SeatGroupCommand, Handler, Validator
  PlaceOrder/   — same pattern
  ...           — one folder per command
Queries/
  ...           — one folder per query, same pattern
Common/         — Result<T>, ValidationBehavior (MediatR pipeline),
                   RestaurantLockBehavior (MediatR pipeline, ordered
                   ahead of ValidationBehavior — see Concurrency above)
```

**`RestaurantSim.Infrastructure/`**
```
Persistence/         — InMemoryRestaurantRepository
Presets/              — JsonPresetRepository<T>
SignalR/               — RestaurantHub, domain-event → SignalR translation
BackgroundServices/     — SimulationTickService
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
— a file's test is always one predictable path away.

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

## What's Still Open (do not assume, ask)

- Save/load, accounts, cloud sync — explicitly deferred until after MVP
  + all DLC modules are complete. Do not build toward these prematurely.
- REST API layer — deferred until an actual external consumer needs
  plain HTTP access. Do not add REST endpoints "just in case."
