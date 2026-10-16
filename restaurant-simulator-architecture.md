# Restaurant Simulator — Architecture

Companion to `restaurant-simulator-business-logic.md` and
`restaurant-simulator-entities.md`. This file documents locked architecture
decisions: solution structure, runtime model, communication strategy, and
long-term platform ambitions that constrain today's decisions even though
they won't be built until later. Nothing here overrides the business logic
or entity docs — it's the structural/technical layer beneath both.

Status legend: 🔒 Locked · 🚧 In progress · ⏳ Not yet discussed

---

## 1. Solution Structure

Separate `.csproj` per layer, referencing inward only:

```
RestaurantSim.Domain          (no references — pure logic, entities, rules)
RestaurantSim.Application     → Domain
RestaurantSim.Modules.*       → Domain, Application (public contracts only)
RestaurantSim.AutoManager     → Domain, Application (public contracts only)
RestaurantSim.Infrastructure  → Application, Domain
RestaurantSim.Api             → all of the above (composition root)
```

**Why separate projects, not folders in one project:** layering becomes a
compile-time rule, not a convention. `Domain` physically cannot reference
`Infrastructure` — there's no project reference available to do it with.
Given the project's "correctness over speed" principle and that an AI
assistant will be writing a large share of the code, an enforced boundary
beats a documented one. Cost is organizational only — no runtime overhead.

**`RestaurantSim.Modules.*`** — one project per DLC-style module (or a
shared `Modules` project, split later if it grows), e.g.
`RestaurantSim.Modules.FoodCritic`. References only `Domain`'s public
contracts (interfaces, events) — proves the `ISimulationModule` extension
pattern is real: if a module needed anything beyond the public surface,
that's a signal the extension point is leaking internals.

**`RestaurantSim.AutoManager`** — the automated decision layer (see §5).
Peer to `Modules`, not nested inside it: modules add new game systems,
AutoManager drives existing ones via the same command surface a human
player uses.

**`RestaurantSim.Api`** is the sole composition root — the only project
allowed to know about every other project and wire them together
(`Program.cs` registers Infrastructure implementations against
Application/Domain interfaces, registers active modules per
`RestaurantConfig.EventModuleIds`, registers AutoManager strategies).
Everything else depends only on abstractions.

**Test projects mirror the layer split:** `RestaurantSim.Domain.Tests`,
`.Application.Tests`, `.AutoManager.Tests`, etc. — not one combined test
project. See §7.

**Root namespace / solution name:** `RestaurantSim` (confirmed).

---

## 2. Simulation Loop

Fixed-tick model via a .NET `BackgroundService`:

- A hosted service loops at a configurable **real-world interval** and
  advances `Restaurant.SimulationTick` by 1 each iteration. The interval
  itself is derived from `RestaurantConfig.TicksPerRealSecond` (mechanics
  doc §14) — one config value controls simulation speed, and every
  duration-bearing preset value is authored in real-world seconds and
  converted to ticks at load time, so changing speed later never requires
  touching game logic or preset content.
- Each tick dispatches whatever tick-based logic needs to run that cycle
  (kitchen progress, discrete Mood events, Mood decay/recovery, complaint
  checkpoints, inter-group spread, movement/arrival resolution) in the
  locked order specified in §2a below — **resolved, backported from
  implementation-phase decisions.**
- **AutoManager does not run inside the tick.** Once a tick finishes and
  releases the per-restaurant lock (§11), AutoManager runs as its own
  sequential pass immediately after — see §11 for the full concurrency
  model and why this ordering was chosen over running AutoManager as part
  of the tick itself.
- `RestaurantConfig.MinutesPerTick` and `TicksPerRealSecond` are
  independent knobs answering different questions: `MinutesPerTick` is how
  much **in-game simulated time** one tick represents (drives the
  restaurant's own clock/day-length); `TicksPerRealSecond` is how many
  ticks execute per **real-world second** (drives engine/animation speed).
  Neither implies the other — a restaurant can simulate "fast" in-game
  time at a "slow" real-world tick rate or vice versa.

---

## 2a. Intra-Tick Execution Order 🔒 (added — backported from implementation-phase decisions)

Resolves §2's previously-open gap: mechanics doc §7 defines *cadence*
(what runs every tick vs. at throttled checkpoints) but never locked the
*order* systems run in within a single tick. Fixed order, five steps:

1. **Resolve scheduled completions for this tick** — kitchen
   `Cooking → Ready`, movement arrivals, bathroom visit conclusions,
   `CustomerCall` stall re-escalation, Arrival's `NextArrivalTick` check,
   path-replan-on-stall.
2. **Continuous per-tick accumulation** — Mood decay/recovery, staff
   Fatigue accrual (§13a of the mechanics doc), Busser cleaning progress.
   Reads state already updated in step 1.
3. **Intra-group Mood spread** — reads this tick's already-decayed Mood
   from step 2, not a stale pre-event snapshot.
4. **Deterministic threshold checks + probability checkpoints** — Queue
   abandonment (deterministic, every tick, not throttled) alongside
   complaint/filing/bathroom-visit rolls (throttled to
   `ProbabilityCheckpointIntervalSeconds` per mechanics doc §7). Inter-group
   spread fires inline here as an immediate reaction to a walkout/loud-
   complaint/filing that occurs in this same step, not as a separate step.
5. **Day/meal-period boundary check** — last, since `DayEndEligible`
   (mechanics doc §15) depends on queue/active-group counts step 4 could
   have just changed this same tick.

**Rationale:** resolve "what changed this tick" before applying "ongoing
background drift," so decay/spread read already-updated phase/presence
rather than a stale pre-event snapshot — e.g. a customer whose food
arrives this tick shouldn't take a "course wait" decay hit the same tick
it arrived.

AutoManager is unaffected by this ordering — it still runs as its own
pass after the tick releases the lock (§11), outside all five steps above.
**Implementation status — all five steps built (backported).**
`RestaurantTickProcessor` dispatches, in order: `AdvanceKitchen` +
`SpawnArrival` (step 1) → `AdvanceMood` + `AdvanceStaffShift` (step 2) →
`SpreadGroupMood` (step 3) → `ProcessQueueAbandonment` (step 4) →
`EndDayCommand` (step 5), every tick, for every restaurant. Busser cleaning
progress (`AdvanceCleaningProgressCommand`, mechanics doc §13) is the step 2
item of that name. Step 3 applies to queued groups as well as seated ones
(mechanics doc §4). Queue abandonment is fully self-contained — no
inter-group spread (mechanics doc §2).

- **Cross-restaurant scheduling:** multiple `Restaurant` instances (§19 of
  the entity doc's multi-tenant model) tick fully independently of each
  other — each restaurant's own per-restaurant lock (§11) naturally
  serializes that restaurant's tick sequence, so one restaurant's tick N+1
  can't begin before tick N (and its AutoManager pass) has finished, but
  restaurant A and restaurant B never block each other. This falls out of
  the lock model already in place — no separate scheduling logic needed.

---

## 3. Client Communication — SignalR-only for MVP

**No REST layer in MVP.** Considered and explicitly rejected for now:

- A persistent SignalR connection is already required for live tick
  events. Invoking a hub method on an *already-open* connection (for
  one-shot needs like "give me current state on connect" or "list
  available menu presets") is **more efficient** than a separate REST
  call would be — no new handshake, no new TCP/TLS setup, no second
  connection alongside the SignalR one. REST would only be more efficient
  for a client that *isn't* already holding a live connection, which
  doesn't describe MVP's own frontend.
- REST's actual value (plain-HTTP access for external tooling, scripts,
  health checks, or future third-party consumers per the "reusable
  engine" ambition) is a real future need, not a current one. Adding
  REST endpoints later is additive — does not require touching Domain,
  Application, or existing SignalR handlers.

**Push strategy — event-based, not full-state-per-tick.** The tick loop
raises domain events (`GroupSeated`, `OrderItemReady`, `MoodChanged` past
a threshold, etc.) via MediatR notifications. An Infrastructure-layer
handler subscribes and translates relevant events into SignalR messages.

**Why event-based:** broadcasting the full `Restaurant` aggregate every
tick is expensive and mostly redundant — most state doesn't change tick
to tick. Event-based keeps `Domain` completely ignorant of transport: it
raises "this happened," and Infrastructure decides whether/how that
becomes a network message. This also scales toward the offline/MAUI
ambition (§6) — local state changes don't need a network layer to matter
there.

---

## 4. Data Access Pattern

**Repository abstraction in front of the aggregate**, even though MVP
persistence is in-memory only:

- MediatR handlers depend on an interface (e.g. `IRestaurantRepository`
  with `Get`/`Save`), never on concrete storage.
- MVP implementation is a trivial in-memory store (session = process
  lifetime; no save/load in MVP — confirmed, see §6).
- **Why now, for something MVP doesn't need yet:** this is what makes
  swapping to real persistence (local save files, later cloud sync)
  purely an `Infrastructure`-layer change — zero rework in `Application`
  or `Domain` when that day comes. Cheap insurance today given
  persistence is an explicit long-term ambition.
- **Concurrency note:** the repository itself doesn't need to be
  thread-safe internally — every `Get`/`Save` round-trip already happens
  inside the per-restaurant lock (§11), so only one caller is ever inside
  a locked section for a given restaurant at a time. See §11 for the full
  model.

---

## 5. AutoManager (Automated Decision Layer)

`RestaurantSim.AutoManager` — the full-automatic-mode capability described
in the business logic doc (§3), formalized as its own project.

- References only the same public command surface a human player uses
  (`SeatGroupCommand`, `AssignWaiterCommand`, etc.) — nothing
  special-cased, nothing reaching into internals a manual player couldn't
  also reach. This is the actual validation that the game's decision
  points are cleanly exposed — if AutoManager ever needed more, that's a
  design smell in the manual path, caught at compile time rather than by
  convention.
- Internally: one heuristic strategy per decision point —
  `ISeatingStrategy`, `IWaiterActionStrategy`, `IKitchenSyncStrategy`,
  `IStaffTaskPriorityStrategy`, etc. — each independently
  swappable/tunable. Opens the door later to difficulty tiers or
  smarter-vs-dumber AI, not just an accessibility toggle, without being
  designed for that now.
- **`IStaffTaskPriorityStrategy`** (added in concurrency-sweep session) —
  governs which eligible task an idle staff member picks next when more
  than one is available (e.g. a Busser choosing between a dirty table and
  an almost-dirty bathroom). Busser's concrete ordering is locked in
  mechanics doc §13; this interface generalizes the same question to other
  roles later.
- **Sequencing and lock granularity** — see §11 for the full model.
  Summary: AutoManager runs as its own sequential pass after each tick
  completes, and acquires/releases the per-restaurant lock **per
  individual decision**, not once for the whole pass — keeps any single
  player command's worst-case wait short (one decision's processing time,
  not a full pass), and lets player commands interleave between
  AutoManager's individual decisions rather than queueing behind the
  entire pass.
- **Per-system toggles:** `Restaurant.AutoManagerToggles` (entity doc §19)
  — session-mutable, defaults all-on, persists across the session (not a
  `RestaurantConfig` preset value, since the player can flip it live). MES
  is deliberately **not** among the toggleable systems — always automated
  regardless of AutoManager's on/off state; the player's only lever over
  MES is the per-ticket priority bump (mechanics doc §10).

- **Layering — `IAutoManagerRunner` (backported from implementation).**
  CLAUDE.md's locked layout puts strategy interfaces (`ISeatingStrategy`,
  etc.) in the `RestaurantSim.AutoManager` project rather than
  `Domain/Abstractions`. That meant `SimulationTickService` (Infrastructure)
  couldn't reach `AutoManager` under the locked dependency graph
  (`Infrastructure → Application, Domain` only). Resolved — not a deviation
  — via `IAutoManagerRunner`, defined in Application, implemented by
  `AutoManagerProcessor` in `AutoManager`, wired in `Api`'s composition
  root. `RestaurantTickProcessor` (Application) calls the interface, never
  the concrete project.
- **Built strategies (Phase 6):** `ISeatingStrategy` (default: greedy
  best-fit, business logic §1.2/§1.3 — drains every eligible queued group
  each pass), `IStaffTaskPriorityStrategy` (mechanics doc §13's tiered
  order) and `IWaiterActionStrategy` (jokes — one joke per pass per
  restaurant, not a drain; jokes are occasional flavor, not a queue to
  clear).
- **Not built, flagged:** reservation-based queue priority (business logic
  §1.1) — nothing creates a `Reservation` or routes one into the queue yet;
  and `IKitchenSyncStrategy` — MES v1 logic sits inline in
  `AdvanceKitchenCommandHandler`, not behind the interface this section
  names (pure refactor, no behavior change). Waiter actions other than jokes
  (soothing/upsell/check-in) aren't covered by anything recorded in the
  implementation log.

---

## 5.5 Event Resolution — `IEventResolutionStrategy` (added — coherence-check pass)

**Not an AutoManager concern** — this governs core `RestaurantEvent`
lifecycle (§21 of the entity doc: when `Status` flips `Active → Resolved`),
which the sim tick itself must evaluate every event's resolution against
regardless of AutoManager's on/off state. Documented as its own section
rather than folded into §5 to keep that distinction clear.

**The problem this solves:** different `RestaurantEvent` types resolve on
fundamentally different conditions — Inspection resolves on a timer,
Celebrity/Critic resolve when a tagged `CustomerGroup` leaves, a future
DLC type (power outage) might resolve when affected stations are restored.
Hardcoding a per-`EventType` branch inside core sim-loop logic would mean
every new DLC event type requires editing `Domain` — directly violating
the project's DLC-isolation principle (CLAUDE.md: "DLC modules... must
slot in without modifying core entities or handlers").

```
IEventResolutionStrategy (Domain, public contract):
    bool IsResolved(RestaurantEvent evt, Restaurant restaurant, long currentTick)
```

- Registered per `EventType` string key via a `Dictionary<string,
  IEventResolutionStrategy>`, wired in `Api`'s composition root — same
  registration shape already used for `EventModuleIds` (§6.2) and
  AutoManager strategies (§5's `TaskCapabilities`-style dispatch).
- **Built-in MVP implementations** (`Domain`, alongside `RestaurantEvent`):
  `DurationBasedResolution` (Inspection — resolves once
  `currentTick ≥ TriggeredAtTick + Data.DurationTicks`) and
  `CustomerGroupBoundResolution` (Celebrity/Critic — resolves once the
  tagged `CustomerGroup.LifecycleState` reaches `Leaving`/gone). See
  entity doc §21 for the full per-type breakdown.
- **A DLC module supplies its own implementation** and registers it
  against its own new `EventType` string key (entity doc §21, now a
  preset key rather than a closed enum — see that entry for why) — zero
  edits to `RestaurantEvent`, the
  two built-in strategies, or the sim-loop code that invokes them. The
  strategy makes no assumption about *what kind* of condition it checks
  (duration, group state, cash threshold, another event's outcome,
  anything `Restaurant`'s public state exposes) — this is what makes a
  new event type's resolution genuinely arbitrary, not just a
  pre-anticipated shape from a fixed set.
- **Deliberately separate from `ISimulationModule`.** This interface only
  answers "is this event over yet" — the event's actual *effects*
  (reputation swings, tagging groups, spawning things) remain
  `ISimulationModule`'s job, already fully general-purpose and proven by
  `FoodCriticModule`/`CelebrityModule`. Together, the two interfaces cover
  both halves of "a DLC event can do anything": what it does, and when it
  stops.

---

## 6. Frontend & Platform Strategy

### 6.1 Chosen: Blazor WASM, MAUI-Hybrid-ready

- MVP ships as a **Blazor WASM** web app — client-side C#/WASM, runs in
  browser, no server dependency for UI logic beyond the SignalR
  connection to the sim engine.
- Razor components are structured to avoid Blazor-Server-only
  assumptions, so they can later be hosted inside a **.NET MAUI Blazor
  Hybrid** shell (native Windows/PC/Android/iOS wrapper around the same
  components) with minimal rework — same language, same components, same
  engine.
- **Why over alternatives:**
  - *Blazor Server* — rejected: ties every interaction to a live server
    connection, ruling out offline play and standalone app packaging
    entirely. Incompatible with the "run it like a game" ambition (§6.2).
  - *Angular/React* — more mature web ecosystems, but forks the project's
    language (C# engine + separate TS frontend) and provides no path to
    mobile without a full second UI rebuild in a separate game engine
    (Unity/Godot). More total surface area for a solo learning project.
  - *Blazor WASM* — single language throughout (depth in C# rather than
    split effort), real path to PC/mobile via MAUI Hybrid, still ships as
    a normal browser-shareable, portfolio-linkable web app today.
- **Known tradeoff, accepted:** Blazor WASM has a real download-size/
  cold-start cost (ships the .NET runtime to the browser). Not
  disqualifying for a management sim not aimed at scale/strangers.
- **Professional relevance (confirmed via research):** Blazor is a real,
  paid, growing niche within the broader .NET/C# market (not a
  standalone job-title category) — UK median salary ~£60,000, US average
  ~$137,000, with industry commentary expecting continued growth into
  2026 as teams consolidate on single-language C# stacks. Portfolio
  framing should lead with "full-stack C#/.NET," with Blazor as evidence
  within that, not as a Blazor-specific job search.

### 6.2 Long-term ambition (post-DLC tier — informs constraints now, not built yet)

Explicitly **not MVP scope**, and only pursued after MVP + all DLC systems
are complete:

- Native PC and Android builds via **.NET MAUI Blazor Hybrid**, reusing
  the same components and engine.
- **Accounts + login**, to support save persistence across devices.
- **Cloud-synced saves**, with **offline-first play** as a hard
  requirement — full gameplay while disconnected, opportunistic
  background sync when a connection is available. Conflict resolution
  strategy (e.g. most-advanced-`SimulationTick`-wins) is a deferred
  design decision, not yet made.
- These are deliberately excluded from MVP: they require an auth/security
  model and a sync/conflict layer that would be premature complexity
  before core systems and DLC content exist.

**What this ambition costs today (the only things locked now to keep the
door open, nothing more):**

1. `Domain` and `Application` remain free of any web-server or auth
   assumption — no `HttpContext`, no assumption of a live connection for
   game-logic correctness (already required by §1's layering rules
   regardless of this ambition).
2. The repository abstraction (§4) exists now, so a future local-save or
   cloud-save implementation is an `Infrastructure`-only addition.
3. Nothing in the `Restaurant` aggregate or config assumes a single
   global save — `RestaurantId: Guid` and the existing multi-instance
   model (per the entity doc) already satisfy this; confirmed as a
   constraint to preserve, not a new change.

No auth scheme, save-format versioning, or sync provider is designed yet
— explicitly future-chat work.

---

## 7. Testing Strategy

- **Framework:** xUnit.
- **Mocking:** NSubstitute (mocking repository/interface dependencies,
  e.g. `IRestaurantRepository`, when testing handlers in isolation).
- **Assertions:** FluentAssertions.
- **Project structure:** mirrors the layer split (§1) — one test project
  per source project, not one combined test project.

**Coverage priority (qualitative, no hard percentage target — a fixed
coverage number tends to produce busywork tests, which cuts against
correctness-over-speed):**

- **Domain — highest priority.** Pure logic, no dependencies, cheapest to
  test, highest value: Mood/Instability/PatienceLevel interactions,
  complaint severity calculation, heat-lamp-vs-reinform decision,
  section-balancing algorithm. This is where the two design sessions'
  carefully-specified rules actually get locked in against regression.
- **Application (MediatR handlers)** — test command handlers against a
  mocked repository: e.g. does `SeatGroupCommand` correctly update
  cluster state, does `PlaceOrderCommand` correctly reject a kids-menu
  violation.
- **AutoManager** — test decision strategies in isolation (given inputs
  X, does `ISeatingStrategy` pick the expected table). Same value
  proposition as Domain tests — validating that the algorithm does what
  was designed.
- **Infrastructure/Api** — lightest coverage. Mostly wiring; test
  non-trivial logic if any ends up living there, otherwise skip
  exhaustive coverage.

---

## 7.5 Statistics Persistence — `ISnapshotStore`

Referenced by the entity doc (§19, §23) as an existing abstraction but never
formally defined until now — closing that gap:

- `ISnapshotStore` (Domain abstraction, alongside `IRestaurantRepository`/
  `IPresetRepository<T>`): `AppendAsync(Guid restaurantId, StatisticsSnapshot)` / `QueryAsync(Guid restaurantId, range)`
  (**backported** — `restaurantId` is an explicit parameter so the store
  manages restaurant association externally instead of a new field on
  `StatisticsSnapshot`, keeping entity doc §23's struct untouched; same
  treatment `IRestaurantRepository` gives `Restaurant`).
  Writes `StatisticsSnapshot` structs *out* of the hot `Restaurant`
  aggregate on a periodic cadence (day-end at minimum), never accumulates
  them on `Restaurant` itself (per entity doc §19's exclusion of
  `SnapshotHistory` from the aggregate).
- MVP implementation is a trivial in-memory list, same tier as
  `IRestaurantRepository`'s MVP implementation (§4) — real persistence is
  an `Infrastructure`-only swap later, no rework upstream.

## 8. Configuration

- Standard ASP.NET Core `appsettings.json` pattern for tick interval,
  SignalR settings, preset folder path, and similar runtime config.

## 9. Preset Loading

- Presets (`RestaurantConfig`, `TableShape`, `MenuItem`,
  `KitchenStationType`, etc.) are **JSON files on disk**, loaded via
  `IPresetRepository<T>` (already referenced in the entity doc).
- **Why JSON-on-disk over embedded resources:** human-editable without
  recompiling — presets will be tuned frequently during design/balance
  passes — and trivially portable to MAUI later (bundled app assets)
  without the friction embedded resources add to iteration/hot-reload
  during development.

---

## Core Principles Reaffirmed (from business logic doc, now given
technical mechanism)

1. **Data-driven, preset-agnostic** → JSON preset files + `IPresetRepository<T>` (§9).
2. **Reusable simulation engine** → layered `.csproj` boundaries enforce
   this at compile time, not just convention (§1).
3. **Auto-manager AI layer** → `RestaurantSim.AutoManager`, using the
   same command surface as manual play (§5).
4. **Config over hardcoding** → `appsettings.json` + JSON presets (§8–9).
5. **Endless simulation** → unaffected by this doc; runtime/architecture
   only.

---

## 10. Build Order (locked)

Sequenced so Domain (zero dependencies) goes first, and a real working
vertical slice exists early rather than building everything horizontally
before anything runs.

**Phase 0 — Scaffolding.** All `.csproj` projects created with correct
references (empty Domain/Application/Infrastructure/Api/AutoManager/
Modules + `.Tests` counterparts). `Nullable` enabled. MediatR,
FluentValidation, NSubstitute, FluentAssertions added. Verify the
reference graph actually enforces layering (an illegal reference should
fail to compile).

**Phase 1 — Domain entities, shape only.** Built in the entity doc's own
dependency order (1→24, plus `Freezer` at §18a alongside `MenuItem`):
`IPlaceable` → `TableShape` → `Table` → `Seat`
→ `TableCluster` → `StaffMember`/`StaffStats` → `CustomerGroup`/
`Customer` → ... → `Restaurant` aggregate last. No business rules yet —
just correct types/nullability.

**Phase 2 — Repository + in-memory store.** `IRestaurantRepository`
(Domain) + trivial in-memory implementation (Infrastructure). Unblocks
everything downstream that reads/writes a `Restaurant`.

**Phase 3 — First vertical slice: `SeatGroupCommand`.** End-to-end:
command → validator → handler → repository → domain event
(`GroupSeated`) → SignalR broadcast. Chosen first because it touches
nearly every architectural piece in the smallest real feature — proves
the skeleton works before more commands are built on an unproven
pattern. Full test coverage here; this slice becomes the template every
future command copies.

**Phase 4 — Core loop, breadth-first by lifecycle stage.** Follows the
business logic doc's own sequence (§1): Arrival → Queue → Seating →
Ordering → Kitchen → Eating → Paying → Leaving. Each stage = its own
command(s), handler(s), tests, in that order. Kitchen (§2) is the most
complex sub-system (course-sync, station contention, heat lamp) — budget
the most time here.

**Phase 5 — Sim loop + tick-driven systems.** `BackgroundService`,
`SimulationTick` advancement, Mood decay/recovery, staff fatigue, and
**meal-period/day boundaries** (mechanics doc §15, added — coherence-check
pass — `EndDayCommand`/`SkipToNextMealPeriodCommand`, staff `OffShift`
clock-in/out) — Fatigue's reset behavior depends on this, so build it
alongside Fatigue rather than after. Wired in after the command-driven
core loop already works, since tick logic mutates the same state the
commands do and is easier to reason about against a working baseline.

**Phase 6 — AutoManager.** Built only once the manual command surface
(Phases 3–5) is proven — AutoManager's whole value is driving that
surface, so it needs to exist and be trustworthy first.
*Status (backported):* `ISeatingStrategy`, `IStaffTaskPriorityStrategy` and
`IWaiterActionStrategy` are complete, tested, and wired into
`AutoManagerProcessor` (§5), which runs after every tick for every
restaurant. Remaining, optional: the `IKitchenSyncStrategy` extraction.

**Phase 7 — Frontend (Blazor WASM).** Can start in parallel with Phase
4–5 once SignalR events exist to consume — optional parallelization, not
a hard sequence dependency.

*Departure choreography note (not a backend logic item — logged so it isn't
lost):* the payer stands, pays, and returns to sit — unless they're the last
payer at the table (a split-billing DLC scenario), in which case they stand
near their seat, pause ~1 second, then the whole group stands and leaves
together. Already supported for MVP (single payer) by existing events:
`BillPaid` (cue: payer pauses near seat) then `GroupLeft` (cue: whole group
stands and exits). "Last payer" only becomes a meaningful question once
split-billing exists — `PayBillCommand` is currently all-or-nothing (one
`PayerId`, one atomic payment), so it would first need a per-`OwnerId`
payment-status concept; not something a UI-side guess can cover.

**Phase 8 — DLC modules, events (Inspection/Critic/Celebrity), stats.**
Last, since these are explicitly post-MVP and designed to slot in
without touching core.

---

## 11. Concurrency Model 🔒 (added in concurrency-sweep session)

Previously entirely unspecified — this section closes that gap. Applies
identically to player commands, AutoManager commands, and the
`SimulationTick` itself; none bypass it.

**Per-restaurant lock:** a per-`RestaurantId` async lock — e.g.
`SemaphoreSlim(1,1)` per restaurant, held in a
`ConcurrentDictionary<Guid, SemaphoreSlim>` — implemented as a MediatR
pipeline behavior ordered **ahead of** `ValidationBehavior`. Held across
the full validate → handle → save sequence for every command, so
validation reads are guaranteed consistent with what the handler goes on
to act on. This is what makes the in-memory `IRestaurantRepository` (§4)
safe without needing to be thread-safe itself.

**Why per-restaurant, not global:** each `Restaurant` instance (§19 of the
entity doc's multi-tenant model) is fully independent — two different
restaurants never contend for the same lock, so this scales cleanly with
however many sessions a host process serves at once (§2).

**Validators must always re-check state at handle-time, never trust a
pre-fetched/stale read.** The lock prevents concurrent corruption, but two
sequential commands can still legitimately change what's true between when
a caller decided to act and when its command actually runs (e.g. two seat
requests for the same table moments apart — the second must see the table
is no longer free, not act on stale information).

**AutoManager sequencing:** AutoManager does **not** run inside the tick —
it runs as its own sequential pass immediately after a tick finishes and
releases the lock. Within its own pass, it acquires and releases the lock
**per individual decision**, not once for the whole pass. This was chosen
over holding the lock for AutoManager's entire pass specifically to keep a
waiting player command's worst-case delay to "one decision's processing
time," not "however many decisions AutoManager happens to be making this
pass."

**Lock fairness — standing note, not an active design.** `SemaphoreSlim`
does not guarantee FIFO acquisition order between a waiting player command
and a waiting AutoManager decision. Per-decision lock granularity (above)
keeps this low-risk in practice. No fairness mechanism is designed or
planned for MVP — revisit only if playtesting surfaces actual input lag
under heavy automation.

**Cross-cutting convention — "check all, then commit all."** Applies
anywhere a command reserves more than one resource, or overrides an
in-progress task/reservation:
- Kitchen: starting an item requires both a free station slot *and* an
  idle cook — both are validated before either is committed (mechanics
  doc §10), avoiding a partial-reservation state if only one is available.
- Movement: a task/movement override must confirm the new destination has
  a valid path *before* releasing the old reservation (mechanics doc §9)
  — an override with no valid path leaves the old reservation and task
  untouched rather than abandoning it into limbo.
- Staff task reassignment (e.g. pulling a Busser off a cleaning task):
  same rule, same reasoning (mechanics doc §13).

This is now a named, general convention — any new command that claims more
than one resource, or replaces an existing commitment, follows this same
shape by default, not just the three cases above.

---

## 12. Current Status

1. Conventions decisions locked (naming, validation approach, nullable
   reference type policy, error/exception handling strategy, Git
   conventions) — see `CLAUDE.md`.
2. Agent file (`CLAUDE.md`) written and locked.
3. Folder/file layout locked — see `CLAUDE.md`.
4. MVP preset content (config, layout, menu) locked — see
   `restaurant-simulator-presets.md`.
5. Implementation is underway — Phases 0–6 of the Build Order (§10) are
   built per the implementation-phase log (Phase 6's optional
   `IKitchenSyncStrategy` extraction remains). Phases 7–8 are not recorded
   here. Implementation-phase decisions have been backported into this
   doc set; see the mechanics doc's §16 for what changed.
