# Restaurant Simulator — Mechanics Reference

Companion to `restaurant-simulator-business-logic.md`,
`restaurant-simulator-entities.md`, and `restaurant-simulator-architecture.md`.
This file locks **concrete, implementable formulas** for systems the other
docs only describe qualitatively — Mood/Patience/Instability math, complaint
probability, tips — plus later sessions' topics (AI pathing, MES/kitchen
scheduling, tick-rate architecture).

Status legend: 🔒 Locked · 🚧 In progress · ⏳ Not yet discussed

---

## 1. Trait Ranges & Generation

- `PatienceLevel`, `Instability`, `SusceptibilityToGroup`: **0.0–1.0**, `float`.
- Generated at spawn via normal distribution around a profile mean (e.g.
  regulars skew `Patience` mean 0.6, first-timers 0.5), `σ ≈ 0.15`, clamped
  `[0,1]`. Fixed for the visit — never mutated after generation.
- Full `float` precision internally. Round to **2 decimals** for display
  (UI/logs/stats) only.

---

## 2. Mood Decay & Recovery

```
PatienceModifier_decay(P)    = (1.6 - 1.2·P) × PatienceDecayBaseline
PatienceModifier_recovery(P) = 0.4 + 1.2·P      // range [0.4, 1.6]
```

`PatienceDecayBaseline` (`RestaurantConfig`, entity doc §22) is a global
scalar over decay only — lets a preset make an entire restaurant's
customers globally more/less patient (e.g. a fine-dining preset vs. a
fast-diner preset) without touching per-customer `PatienceLevel`
generation. Defaults to `1.0` (no change vs. the base curve) if unset.

Base rates — **corrected, coherence-check pass: named as real
`RestaurantConfig` fields instead of hardcoded literals called
"preset-tunable" with nothing actually backing them** — applied every
tick:
| Source | Rate |
|---|---|
| Queue wait | `QueueWaitMoodDecayRate × PatienceModifier_decay` (default `-0.4`) |
| Course wait | `CourseWaitMoodDecayRate × PatienceModifier_decay` (default `-0.3`) |
| Natural recovery (seated/eating) | `NaturalMoodRecoveryRate × PatienceModifier_recovery` (default `+0.5`) |

Discrete events (flat, not per-tick):
| Event | Delta |
|---|---|
| Wrong/cold item (severity-scaled) | `-8` to `-20` |
| Waiter action success | `+5` to `+15` |
| Waiter action failure | `-3` to `-10` |

Clamp `Mood ∈ [0,100]` after every apply.

**Mood tiers:**
| Tier | Range |
|---|---|
| High | 70–100 |
| Mid | 40–69 |
| Low | 15–39 |
| Very Low | 0–14 |

---

## 3. Complaint Probability & Severity

```
InstabilityMultiplier(I) = 0.5 + 1.5·I     // range [0.5, 2.0]
```

Base per-checkpoint chance by tier:
| Tier | Base chance |
|---|---|
| Mid | 0.05 (polite ask) |
| Low | 0.15 (complain) |
| Very Low | 0.35 (loud/send-back/book-eligible) |

```
P(complain) = min(0.9, BaseChance(tier) × InstabilityMultiplier(I))
P(loud | complain) = I
```

Both formulas above are core — universal Mood-driven customer behavior,
no `ComplaintRecord`/DLC module involved.

**Everything below is owned by `ComplaintsBookModule` (DLC, entity doc
§11 — moved out of core in the coherence-check pass).** Inactive by
default (`EventModuleIds: []`, presets doc); with it off, complaining/
loud-complaining above still happens exactly as specified, but nothing
below fires and Inspection has no complaint-driven skew.

**`ComplaintRecord.Severity`:**
```
Severity = CategoryBaseWeight × (1 - Mood/100) × (0.5 + 0.5·I)
```

**Complaints-book filing** (Very Low tier only):
```
P(file) = min(0.3, 0.05 × (1 - Mood/100) × InstabilityMultiplier(I))
```

---

## 4. Intra-Group Mood Spread

```
InfluenceWeight = IsAdult ? 1.5 : 1.0
GroupWeightedAvg_i = Σ(Mood_j × InfluenceWeight_j) / Σ(InfluenceWeight_j)   // j ≠ i

SpreadRate = 0.05/tick   // 0.075/tick if full-group avg Mood < 30 (tipping point)

MoodDelta_i = SusceptibilityToGroup_i × (GroupWeightedAvg_i - Mood_i) × SpreadRate
```

---

## 5. Inter-Group Mood Spread

Fires only on visible events, targets adjacent `TableCluster`s (grid
adjacency, reuse section-assignment logic).

```
P(spread):
  Walkout        = 0.40
  Loud complaint  = 0.25
  Book filing     = 0.15

Magnitude = BasePenalty(eventType) × AvgSusceptibility(nearby group)
  Walkout base        = -10
  Loud complaint base = -6
```

---

## 6. Tip Calculation

```
TipPercentage(Mood) = TipBaseRate × (Mood/100)^TipMoodExponent × SpendingModifier
SpendingModifier: Budget = 0.85, Normal = 1.0, Generous = 1.15

TipPercentage = clamp(TipPercentage, 0, 60)
TipAmount = (TipPercentage / 100) × Order.Items.Sum(PriceAtOrder)
```

Guarantees `Mood = 0 → TipAmount = 0`. `TipBaseRate` (default `50`) and
`TipMoodExponent` (default `0.75`) are both `RestaurantConfig` fields
(entity doc §22) — not hardcoded.

Reference curve (Normal tendency): Mood 0→0%, 10→~9%, 50→~30%, 90→~46%, 100→50%.

---

## 7. Evaluation Cadence

- **Mood decay/recovery**: applied every `SimulationTick`.
- **Probability rolls** (complaint, filing, inter-group spread): throttled to
  discrete checkpoints, not evaluated per-tick. Reuses `CustomerGroup`'s
  existing `PhaseWaitState`/`NextAutoVisitTick` fields for wait-based checks;
  `ProbabilityCheckpointIntervalSeconds` (`RestaurantConfig`, entity doc
  §22 — **added, coherence-check pass**, converted to ticks per §14),
  default 3 seconds, as a starting value. Also rolled immediately on any
  discrete Mood-changing event (food arrives, waiter interaction resolves).
- This section defines *cadence* only (what runs every tick vs. at
  checkpoints) — the *order* these systems run in within one tick is
  locked separately in architecture doc §2a (**resolved, backported from
  implementation-phase decisions**).

---

## 8. Tick-Rate Architecture 🔒

Single `SimulationTick` stays authoritative for **everything**, including
movement. No second, faster server tick. Current default is 1 tick = 1 real
second, but this is now a config value, not a hardcoded assumption — see
§14 (`TicksPerRealSecond`) for how simulation speed can change without
touching any formula or preset content in this document.

- Pathfinding runs **once** at dispatch. Server emits `MovementStarted`:
  `{ EntityId, Path, StartTick, DurationTicks }` — `DurationTicks` derived
  from path length × entity `Speed`.
- **Client (Blazor) interpolates position locally** between waypoints for
  smooth visual movement — server has no involvement between dispatch and
  arrival.
- Server schedules arrival logic for `StartTick + DurationTicks`, same
  mechanism as any other tick-scheduled event (kitchen ticket ready, etc.).
- Movement smoothness is a **rendering concern**, not a simulation-rate
  concern — keeps Domain transport-ignorant, no extra ticking
  infrastructure, no full-state-per-tick broadcast.

---

## 9. AI Pathing 🔒

**Cooperative pathfinding via space-time reservation** — solves the "two
independently-dispatched paths conflict" problem without re-simulating
existing in-flight paths.

- `MovementReservationStore` (Infrastructure, in-memory):
  `Dictionary<(GridPosition, long Tick), EntityId>`. Populated at dispatch,
  entries expire as ticks pass or the path completes/is cancelled.
- **Cleanup — active, tick-bucketed, not a full scan:** a companion index,
  `Dictionary<long Tick, List<GridPosition>>`, groups reservations by their
  tick. Each `SimulationTick`, only that tick's bucket is removed from both
  structures — cost is proportional to how many reservations expire this
  tick, not the total store size. Prevents both a slow memory leak over a
  long endless-simulation session and the growing cost of a full-store scan
  every tick.
- **Cancellation is immediate and unconditional on the *old* reservation,
  but only after the *new* action is confirmed valid.** A task/movement
  override (e.g. reassigning a Busser mid-walk) must validate the new
  destination has a real path **before** releasing the old reservation —
  see §13 for the same "check all, then commit all" rule applied to
  cleaning-task overrides. An override that turns out to have no valid
  path leaves the old reservation and task untouched, rather than
  abandoning it into limbo.
- **No replanning of other entities' paths when a reservation frees up
  early.** Considered and rejected for MVP: when an entity's task is
  cancelled and its reservations release ahead of schedule, other in-flight
  paths that had detoured around it are *not* proactively replanned to take
  the now-shorter route. The existing **replan-on-stall** rule below
  already recovers the cases that matter (a path stuck waiting long enough
  to be worth fixing); the marginal gain from also replanning merely
  suboptimal-but-not-stalled paths is small relative to the cost (tracking
  which paths detoured around what, plus risking a visible mid-animation
  snap on the client). Revisit only if playtesting shows this feels
  noticeably wrong.
- `IPathfindingService.FindPath(start, end, entitySpeed)` — **space-time
  A\***: each search node is `(cell, tick)`, not just `cell`. Expanding a
  neighbor checks the reservation table in addition to the static
  furniture/station footprint grid. A cell blocked at a given tick costs
  one extra "wait" tick rather than failing the search outright.
  **Footprint resolution — added, DLC-extensibility pass:** for any
  `IPlaceable`, the blocked-cell set is `OccupiedCells ?? all cells within
  Hitbox` (entity doc §1) — a single fallback checked uniformly regardless
  of shape. Every current MVP entity has `OccupiedCells == null`, so this
  resolves to the plain rectangle exactly as before; a future non-rectangular
  DLC object is handled by the same code path with zero pathfinder changes.
- `TicksPerCell` derived from entity `Speed` — turns a path into concrete
  tick-stamped reservations.
- Existing in-flight paths are never disturbed — only the newly-dispatched
  path plans around what's already reserved.

**Replan on stall:** if a path's wait-queue exceeds
`PathReplanMaxWaitSeconds` (`RestaurantConfig`, entity doc §22 — **added,
coherence-check pass**, converted to ticks per §14, default 5 seconds),
trigger a full replan from the entity's current position rather than
waiting indefinitely.

**Movement is 8-directional**, not 4 — diagonal steps allowed.
- **Diagonal cost:** `√2` per diagonal move vs `1` orthogonal (standard A*
  weighting — prevents diagonal paths being artificially cheap/staircased).
- **Corner-cutting rule:** given a 2×2 block `[A B / C D]`, a diagonal move
  C→B (or A→D) is only valid if **at least one** of the two adjacent
  orthogonal cells (A or D) is unblocked/unreserved at that tick. Prevents
  two entities from diagonally phasing through the same blocked corner from
  opposite sides.
- **Rule applies uniformly** to both furniture/station footprints and other
  people — no separate case for static vs. dynamic obstacles, and no
  separate case for rectangular vs. non-rectangular footprints (both
  resolve through the same `OccupiedCells ?? Hitbox` rule above).

**Person hitbox:** 1×1 grid cell (no personal-space bubble in MVP) —
furniture/stations keep their real footprints as already defined via
`IPlaceable` (rectangular for every current MVP entity, non-rectangular
shapes reserved for DLC per entity doc §1).

**Path smoothing (rendering-only, post-process):** after space-time A*
returns a collision-safe waypoint sequence, run a **string-pulling /
line-of-sight simplification pass** — if the entity can travel from
waypoint N to N+2 in a straight line without crossing a blocked/reserved
cell, drop waypoint N+1; repeat. Collision/reservation logic keeps thinking
in grid cells (deterministic, testable); the client receives a shorter
waypoint list and lerps straight segments between them — avoids visible
grid-snapping without touching Domain.

---

## 10. MES / Kitchen Scheduling 🔒 (v1 — Simple)

**Versioning:** this is **MES v1**, deliberately simple — single cook per
item, no interruption handling. Scheduling logic lives behind
`IKitchenSyncStrategy`, so a future **MES v2** (multiple cooks
parallel-working the same item/station with stacked `Speed`, staff
`Resting`/break interrupting an in-progress item) is a strategy swap, not a
rework of `OrderItem`/`KitchenTicket`. v2 is not designed yet — this section
covers v1 only.

**`StaffStats` range:** `Speed`, `ErrorRate`, `Fatigue`, `Charisma` all
**0.0–1.0**, baseline `0.5` = "average" (no modifier vs. authored
`PrepTimeSeconds`/preset values).

### Effective prep time

```
CookSpeedMultiplier(S) = 1.6 - 1.2·S        // [0.4, 1.6]
FatigueMultiplier(F)   = 1.0 + 0.5·F        // [1.0, 1.5]

PrepTicks = max(1, round(MenuItem.PrepTimeSeconds × TicksPerRealSecond))   // §14 conversion + rounding rule

EffectivePrepTicks = PrepTicks
                    × KitchenStationType.SpeedMultiplier
                    × CookSpeedMultiplier(Cook.Speed)
                    × FatigueMultiplier(Cook.Fatigue)
```

### Course-sync scheduling

```
TargetReadyTick = SubmittedAtTick + max(EffectivePrepTicks_i)   // slowest item sets the target
StartTick_i     = TargetReadyTick - EffectivePrepTicks_i        // each item starts late enough to finish together
```

Actual start can slip past `StartTick_i` under station contention —
`ActualReadyTick_i = ActualStartTick_i + EffectivePrepTicks_i` can exceed
`TargetReadyTick`. That gap is what threatens sync and triggers the
heat-lamp-vs-reinform decision below.

### Station contention — Oldest-Ticket-First

When a station's slots are full, queued items wait ordered by their
**owning `KitchenTicket.SubmittedAtTick`** (oldest ticket first) — not by
raw item queue-entry order, and not full EDF-by-deadline (locked, simpler
v1 behavior). This differs from plain FIFO in one case: course-sync (see
below) can delay a fast item's `StartTick_i` so it finishes alongside a
slower item on the same ticket — meaning a fast item from an older ticket
can become eligible for a station *later* than a fast item from a newer
ticket. Oldest-ticket-first resolves that case in favor of the older
ticket; plain FIFO would not.

### Heat lamp vs re-inform decision

For an item that finishes early relative to the ticket's other item(s):

```
HoldTicks_i = ActualProjectedReadyTick(slowest remaining item) - ActualReadyTick_i

MaxAcceptableWaitTicks = EffectiveTicks(MaxAcceptableWaitSeconds)   // §14 conversion
ShortDelayThresholdTicks = EffectiveTicks(ShortDelayThresholdSeconds)   // §14 conversion

WaitPressure = clamp((CurrentTick - SubmittedAtTick) / MaxAcceptableWaitTicks, 0, 0.7)
EffectiveThreshold = ShortDelayThresholdTicks × (1 - WaitPressure)   // ShortDelayThresholdSeconds open, pending playtesting

Decision:
  HoldTicks_i ≤ EffectiveThreshold AND HeatLamp has free slot → hold under lamp
  otherwise                                                    → re-inform, serve ready items now
```

**Walkthrough example:** ticket submitted at tick 100 with item A (fast) and
B (slow). Plan: both finish together at tick 110. In reality, A finishes at
106 (contention), but B — also delayed — is now projected for 115, not 110.

```
HoldTicks_A = 115 - 106 = 9
WaitPressure = clamp((106 - 100) / 20, 0, 0.7) = 0.3
EffectiveThreshold = 6 × (1 - 0.3) = 4.2
```
`9 > 4.2` → **re-inform.** Waiter tells the table B is still coming, A is
served now. Ticket flips to **async mode** — once re-informed, every later
item on the ticket is served immediately on `Ready`, no further
hold-evaluation per item (the table's already been told food is arriving
separately, so there's nothing left to protect by holding).

If A had instead finished at 108 with B projected for 111,
`HoldTicks_A = 3 ≤ 4.2` → **hold under lamp**, both served together at 111.

`WaitPressure` is what implements "an already-impatient table skews toward
serve-now" — the same 9-tick hold might be approved early in a visit but
rejected later, purely because the table's total wait has grown.

### Food quality — bool, mutually exclusive with negative events, decay-linked

```
SkillScore = clamp(Cook.Speed × (1 - Cook.ErrorRate), 0, 1)

OrderItem.IsHighQuality: bool   // rolled once, at Status → Ready (i.e. when
                                 // cooking finishes, not when it starts)
P(IsHighQuality) = BaseHighQualityChance × (0.5 + 1.5·SkillScore) × (HeadChefPresent ? 1.3 : 1.0)   // BaseHighQualityChance = 0.05, open pending playtesting
```

**Mutually exclusive with negative food-quality events** — the
`FoodQualityEvent` roll fires immediately after, at that same
`Cooking → Ready` transition (not at a separate pipeline stage). A
high-quality roll skips the negative-event roll (hair, wrong temp,
undercooked) entirely for that item; only non-high-quality items are
eligible for the negative roll:
```
EffectiveErrorRate = Cook.ErrorRate × (1.0 + 0.5·Fatigue) × (HeadChefPresent ? 1.0 : 1.5)
P(FoodQualityEvent | !IsHighQuality) = 0.03 × EffectiveErrorRate   // BaseEventChance = 0.03, open pending playtesting
```

**Decay — high-quality items decay slower under the lamp:**
```
EffectiveDecayRate = HeatLamp.QualityDecayRatePerTick × (IsHighQuality ? 0.5 : 1.0)
BaseQuality = IsHighQuality ? 100 : (FoodQualityEvent ? 30 : 80)
QualityAtServe = clamp(BaseQuality - HeldTicks × EffectiveDecayRate, 0, 100)
```

**Mood impact at serve:**
```
QualityAtServe ≥ 90 → MoodBonus = +10   (high quality, noticeably great)
QualityAtServe ≥ 60 → MoodBonus = +0    (standard, folds into normal recovery)
QualityAtServe < 60 → MoodBonus = -8    (degraded)
```
Extends `QualityAtServe`'s documented role (previously fed only the
negative-event chance) to also directly bump Mood at serve time.

### Plating & delivery — manual transport, two legs

- **Cook → pass point** (in-kitchen shuffle): flat `PlatingTicks =
  EffectiveTicks(PlatingTimeSeconds)` (1–2 seconds authored, preset-tunable),
  added once an item hits `Ready`, before it's available for pickup. Small,
  fixed — not worth full pathfinding for a few in-kitchen tiles.
- **Pass point → table** (the actual carry): real transport, reuses the §9
  space-time pathfinder. Once the hold/re-inform decision resolves, a
  `DeliverOrderItem(s)` movement task dispatches to the assigned waiter, path
  computed from the kitchen pass point to the table's `AnchorPosition`.
  Duration is genuine travel time — a table far from the kitchen actually
  waits longer, a real layout-driven consequence.
- `HeldTicks` (lamp decay) stops accumulating once the item is **picked up**
  for delivery, not once it **arrives** at the table — the hold clock is
  about kitchen-side wait, delivery is separate execution time layered after
  the decision, not before it.

### Player priority bump (manual MES override)

Since MES itself is never toggled off (§ architecture doc §5), the player's
only lever over kitchen scheduling is a **per-ticket priority flag**, set
via `PrioritizeTicketCommand` (**named — coherence-check pass**, resolves
the previously-missing command name):

```
KitchenTicket.PlayerPrioritized: bool   // default false, player-settable
```

A prioritized ticket's items are treated as if their `SubmittedAtTick` were
earlier than any non-prioritized ticket currently contending for the same
station — i.e. it jumps to the front of oldest-ticket-first ordering
regardless of actual submission time, but only relative to other tickets;
it does not bypass station capacity or an already-`Cooking` item. Only one
priority level exists in MVP (on/off, no numeric weight) — a genuine
priority *queue* (multiple ranked levels) is a DLC-tunable extension, not
built now.

### Open, pending playtesting (not guessed)

`ShortDelayThresholdSeconds`, `MaxAcceptableWaitSeconds`, `BaseEventChance`,
`BaseHighQualityChance` coefficients, `PlatingTimeSeconds` — same treatment as
`PatienceDecayBaseline`: real tunables, deliberately left as starting
defaults above rather than final numbers, to be resolved once the sim
actually runs.

---

## 11. Staff-Initiated Jokes & Joke Fatigue 🔒

Extends the waiter-action success/failure model (§2) with autonomous
triggering (AutoManager only, gated by the waiter-action strategy toggle),
per-person magnitude scaling by current Mood tier, and a fatigue curve that
makes repeated jokes at the same table progressively less effective — and
progressively worse to fail.

### Trigger (AutoManager only)

Evaluated per table, per AutoManager pass — not every `SimulationTick`:

```
P(staff notices & jokes) by table's lowest-Mood member's tier:
  Very Low  → 0.20
  Low       → 0.12
  Mid       → 0.04
  High      → 0.01
```

**Cooldown:** a table is ineligible for another *autonomous* joke for
`AutoJokeCooldownTicks` — derived from the preset-authored
`AutoJokeCooldownSeconds` per §14, open/pending playtesting — after the
most recent joke at that table — from either AutoManager **or** the
player. Player jokes are never blocked by this cooldown themselves, but
they still update the timer, so AutoManager won't immediately stack a
joke right after the player already spent one on that table.

### Targeting

- **Player:** explicit choice via UI — a specific customer, or the whole
  table.
- **AutoManager:**
  ```
  One member ≥ MoodGapThreshold below the rest → target that person
  Otherwise (no clear outlier)                 → target the table
  ```
  `MoodGapThreshold` open, pending playtesting.

### Roll — one shared outcome per joke, regardless of targeting mode

```
JokeFatigueMultiplier(JokeCount) = max(0.2, 1 - 0.25 × JokeCount)
EffectiveCharisma = WaiterCharisma × JokeFatigueMultiplier(TableCluster.RecentJokeCount)

RollTargetMood = targeted person's Mood         (person-targeted)
                 lowest-Mood member's Mood       (table-targeted)

Single success/failure roll: EffectiveCharisma vs RollTargetMood
```

### Magnitude — applies to every member currently `IsPresentAtTable == true`

```
DeltaRange by that member's OWN current Mood tier:
  Success: Very Low +10 to +20 | Low +7 to +15 | Mid +5 to +12 | High +3 to +8
  Failure: Very Low  -5 to -14 | Low  -4 to -11 | Mid  -3 to -9 | High -2 to -6

If member is the specifically-targeted person (person-targeted mode only):
  Delta × 1.5
Table-targeted mode: no multiplier — every present member gets the plain
per-person tiered delta.
```

Absent members (`IsPresentAtTable == false` — bathroom, etc.) are excluded
entirely from step application, per §12.

### Fatigue tracking

```
TableCluster.RecentJokeCount += 1   on every joke (player or AutoManager)
TableCluster.RecentJokeCount = 0    when the cluster fully clears (State
                                     transitions NeedsCleaning → Free) —
                                     fatigue is a within-visit resource,
                                     not persistent across parties
TableCluster.LastJokeTick = CurrentTick   on every joke — feeds only the
                                           AutoManager cooldown check above
```

**Open, pending playtesting:** `AutoJokeCooldownSeconds` (preset-authored;
`AutoJokeCooldownTicks` is its derived form per §14), `MoodGapThreshold`.

---

## 12. Bathroom Mechanics 🔒

Applies to **customer** bathrooms (gendered — Male/Female). The staff
bathroom is unisex and only affects staff availability (a brief
hygiene/break unavailability window) — it has no Mood interaction and is
out of scope for this section.

### Trigger — when does a customer decide to go? (added, coherence-check pass)

Previously undefined — the visit *mechanics* below existed, but nothing
ever specified what causes a visit to start. Evaluated at the same
checkpoint cadence as complaint rolls (§7), per present customer:

```
Eligible only if:
  Customer.IsPresentAtTable == true
  AND (LastBathroomVisitEndTick == null
       OR CurrentTick - LastBathroomVisitEndTick >= BathroomCooldownTicks)

MealStateModifier = IsActivelyEating ? EatingMealtimeVisitModifier : 1.0
  // IsActivelyEating = customer is currently consuming a served course
  // (a per-person eating timer running, business logic §1.6). Waiting —
  // queued, waiting for food, between courses, post-meal pre-bill — uses
  // the normal (1.0) modifier instead.

VisitCountModifier = 1 / (1 + ln(1 + BathroomVisitCount))
  // Logarithmic decay: first visit (Count=0) unaffected (modifier=1),
  // each subsequent visit progressively less likely, never hitting a
  // hard zero — matches "the higher the counter the less times they go
  // again" rather than a flat once-per-visit cap.

P(go) = clamp(BaseBathroomVisitChance × MealStateModifier × VisitCountModifier, 0, 1)
```

On a successful roll: `BathroomVisitCount += 1`, visit proceeds per the
composition below. `LastBathroomVisitEndTick` is set when the visit
concludes (on return to table, after the Mood delta below applies) — the
cooldown is measured from *end* of the previous trip, not its start.
`BaseBathroomVisitChance`, `BathroomCooldownSeconds` (→ ticks per §14),
and `EatingMealtimeVisitModifier` are all `RestaurantConfig` fields
(entity doc §22) — open, pending playtesting.

### Visit composition

```
BathroomVisit:
  UseFacilities: bool   // per-visit probabilistic roll — may be skipped
  WashHands: always true

FacilitiesTimeTicks = EffectiveTicks(Customer.Gender == Male
                        ? FacilitiesTimeSecondsMale : FacilitiesTimeSecondsFemale)
WashHandsTimeTicks   = EffectiveTicks(Customer.Gender == Male
                        ? WashHandsTimeSecondsMale : WashHandsTimeSecondsFemale)

Duration = (UseFacilities ? FacilitiesTimeTicks : 0) + WashHandsTimeTicks
```

Both sub-step durations are **gendered**, selected via `Customer.Gender`
(entity doc §9) at the moment a visit starts — Female average exceeds Male
average for both `UseFacilities` and `WashHands`. Concrete values open,
pending playtesting. `EffectiveTicks` is the §14 seconds→ticks conversion.

### Presence & Mood while away

```
On leaving the table (queueing or inside):
  Customer.IsPresentAtTable = false
  Mood FROZEN — no decay, recovery, or intra-group spread (§4) while away

On return:
  Mood delta based on Bathroom.IsClean AT THE MOMENT OF RETURN:
    Clean → +3 to +6
    Dirty → -4 to -8
  Mood resumes normal decay/recovery/spread immediately after
  Customer.IsPresentAtTable = true
```

Absent customers are also excluded from joke resolution (§11) and from the
group-presence requirement for group-wide calls (business logic §1.55).

### Cleanliness

```
Bathroom.CleanlinessScore: float (0–100)
Bathroom.IsClean (derived): CleanlinessScore >= DirtyThreshold

On each use: chance-based decay roll reduces CleanlinessScore by a random
amount (open, pending playtesting) — probabilistic per-use degradation,
not a flat per-tick timer.

Score only ever increases via:
  - Busser cleaning task (§13), progressively, or
  - End-of-day processing → CleanlinessScore = 100, unconditional
```

**Two-tier thresholds** (mirrors the heat-lamp/re-inform pattern of giving
a decision window rather than a hard cutoff):

```
AlmostDirtyThreshold > DirtyThreshold

AlmostDirtyThreshold — triggers Busser cleaning eligibility (§13)
DirtyThreshold        — below this, customers get the Dirty Mood penalty
```

Gives lead time: a bathroom can be flagged for cleaning before it's
actually costing customers Mood.

---

## 13. Busser Duration-Task Formulas (Table & Bathroom Cleaning) 🔒

Reuses the Cook speed/fatigue curve shape from §10, applied to the Busser
role — same mechanism, different stat owner.

```
BusserSpeedMultiplier(S)   = 1.6 - 1.2·S      // [0.4, 1.6]
BusserFatigueMultiplier(F) = 1.0 + 0.5·F      // [1.0, 1.5]

EffectiveCleaningTicks(BaseTicks, Busser) =
    BaseTicks × BusserSpeedMultiplier(Busser.Speed) × BusserFatigueMultiplier(Busser.Fatigue)

ProgressPerTick = 100 / EffectiveCleaningTicks(BaseTicks, Busser)
```

- **Table cleaning:** `BaseTicks` derived from the preset-authored
  `TableCleaningBaseSeconds` (× `TicksPerRealSecond`, per §14) — open,
  pending playtesting for the actual seconds value. Progress applies to
  the individual `Table.IsClean` state — cleanliness is per-`Table`, not
  per-`TableCluster` (see §15 doc-sync notes — this is a structural
  change to the entity doc). Completes (`IsClean = true`) at
  `Progress ≥ 100`.
- **Bathroom cleaning:** `BaseTicks` derived from the preset-authored
  `BathroomCleaningBaseSeconds` the same way — open, pending playtesting.
  Progress applies directly to `Bathroom.CleanlinessScore`.

**Interruption & resume:** progress persists in the resource's own state
regardless of interruption direction (table ↔ bathroom, either way).
Resuming later — same or a different Busser — continues from the saved
progress, never restarts from zero. Applies uniformly to both task types.

**Multi-table cluster cleaning order:** a Busser cleans a merged cluster's
member `Table`s one at a time, in spatial order along the cluster's merge
axis (reuses the same-axis geometry already defined for merging) — "from
one side to the other," not simultaneously, not arbitrarily ordered.
Uninterrupted unless overridden mid-way, per the interruption rule above.

**Busser task-priority (idle Busser choosing its next task):** — revised,
coherence-check pass. Previously, a table always outranked a bathroom
regardless of how dirty the bathroom actually was — even one already past
`DirtyThreshold` (the tier that's actively costing customers Mood on
return, mechanics doc §12/entity doc §24). That's now split into two
tiers instead of one:

```
1. Currently mid-task (table or bathroom)? → finish it — commits until
   explicitly overridden by the player, regardless of what becomes
   eligible afterward
2. Any Bathroom.CleanlinessScore <= DirtyThreshold (URGENT — actively
   costing customer Mood, not just cleaning-eligible), AND fewer than
   Bathroom.MaxConcurrentCleaners Bussers are already assigned to it?
                                             → take it (urgent bathrooms
                                                now outrank tables)
3. Any Table.IsClean == false?              → take it (tables still beat
                                                a merely cleaning-eligible
                                                bathroom — step 4 below)
4. Else, any Bathroom.CleanlinessScore
   <= AlmostDirtyThreshold (and under MaxConcurrentCleaners)?
                                             → take it
5. Else                                     → Idle
```

**`Bathroom.MaxConcurrentCleaners`** (entity doc §24, new — coherence-check
pass): caps how many Bussers can usefully work the same bathroom at once,
so a third Busser doesn't pile onto a bathroom two others already have
covered, even in an urgent state. **Not required for MVP's default
strategy** — a simple MVP implementation can ignore it and let any number
of idle Bussers pile onto the same urgent bathroom — but the field exists
now so a tuned/DLC `IStaffTaskPriorityStrategy` can respect it without a
schema change later, same treatment as `OccupiedCells` (entity doc §1).

Fixed in code for MVP, not player-configurable — matches the treatment of
other AutoManager decision heuristics at this stage (see architecture doc
§5's `ISeatingStrategy`/`IKitchenSyncStrategy` pattern; this is the same
shape, `IStaffTaskPriorityStrategy`, applied to Bussers).

---

## 13a. Staff Fatigue Accrual (added — real gap, not just a backport)

**Previously undefined anywhere.** Business logic §3 states `Fatigue`
"rises over a shift," and §10/§13 already define how `Fatigue` *degrades*
Cook/Busser performance once it exists — but no formula ever specified
how `Fatigue` actually accumulates tick-to-tick. Every other stat at
least has a first-pass curve; this one didn't. Conceptually decided,
coefficients intentionally left open rather than guessed:

```
FatigueDelta_per_tick =
  0                                          if State == OffShift
  BaseFatigueAccrualRate                     if State != OffShift, not held past ShiftEnd
  BaseFatigueAccrualRate × OvertimeFatigueMultiplier
                                              if held past their own ShiftEnd
                                              by the coverage rule (entity doc §6/§24a)

Fatigue = clamp(Fatigue + FatigueDelta_per_tick, 0, 1)   // StaffStats range, entity doc §7
```

- **Overtime, not slower recovery:** confirmed — a staff member held past
  their own `ShiftEnd` by the §24a coverage rule (no other same-role
  staff available to relieve them) accrues `Fatigue` **faster**, not
  slower, matching the real cost of understaffing rather than treating
  overtime as free.
- `BaseFatigueAccrualRate` and `OvertimeFatigueMultiplier` are both
  `RestaurantConfig` fields (entity doc §22) — open, pending playtesting,
  same treatment as `PatienceDecayBaseline`. Do not guess numbers for
  either; flag as pending if asked.
- Resets to `0` on the `Busy`/`Idle → OffShift` transition at `ShiftEnd`
  (entity doc §6/§24a) — unaffected by this section, already locked.

**Status:** decided/confirmed conceptually during implementation-phase
discussion; exact coefficients not yet chosen. Belongs to Phase 5 (sim
loop + tick-driven systems, architecture doc §10) — does not block
Phase 4's remaining lifecycle stages (Queue, Ordering, Kitchen, Eating,
Paying, Leaving), none of which depend on Fatigue accruing correctly yet.

---

## 14. Tick-Rate Configuration 🔒

Resolves the tick-length question left open at §8: `RestaurantConfig`
carries one global value, `TicksPerRealSecond`, and every duration-bearing
preset value in the game is authored in **real-world seconds**, not raw
tick counts — converted once, at preset-load time:

```
EffectiveTicks = max(1, round(AuthoredSeconds × TicksPerRealSecond))
```

**Rounding rule, explicit:** round-half-up, with a hard floor of **1 tick
minimum** — no duration ever resolves to `0` ticks (an instant, free
action), regardless of how short the authored value is relative to
`TicksPerRealSecond`. Round rather than floor/ceil specifically to avoid a
systematic bias: floor would make every fractional-tick duration
consistently shorter than authored, ceil consistently longer — round
averages out to unbiased across the full set of durations in a preset.

This applies uniformly to every duration this document defines a tunable
for: `MenuItem.PrepTimeSeconds` (replaces raw `PrepTicks` as the authored
preset field), `TableCleaningBaseSeconds`, `BathroomCleaningBaseSeconds`,
both bathroom visit sub-step durations, `AutoJokeCooldownSeconds`,
`ShortDelayThresholdSeconds`, `MaxAcceptableWaitSeconds`,
`PlatingTimeSeconds`, and any future duration value added to a preset.

**Why this matters:** changing simulation speed later (e.g. running 4×
faster) becomes a single config edit — `TicksPerRealSecond: 1 → 4` — with
zero changes to any preset file or formula in this document. Every
formula here already operates on the *derived* `EffectiveTicks`, not the
authored seconds value, so nothing downstream needs to know the conversion
happened.

**Current default:** `TicksPerRealSecond = 1` (1 tick = 1 real second) —
confirmed MVP baseline, not a placeholder pending a different decision.

**Tick resolution vs. perceived game speed — clarified, coherence-check
pass.** `MinutesPerTick` and `TicksPerRealSecond` remain independently
authored fields (§2 of the architecture doc — "neither implies the
other"), but a preset author who wants **finer tick resolution without
changing how fast the game feels to play** should jointly tune them
rather than changing one in isolation:

```
MinutesPerTick = DesiredInGameMinutesPerRealSecond / TicksPerRealSecond
```

Example: MVP default is 1 in-game minute passing per real second
(`MinutesPerTick=1`, `TicksPerRealSecond=1`). Moving to
`TicksPerRealSecond=4` (ticks every 0.25 real seconds — smoother
movement/finer kitchen-scheduling granularity) while keeping the same
*perceived* pace requires `MinutesPerTick=0.25`, so 4 ticks still add up
to exactly 1 in-game minute per real second — unchanged from before. This
isn't a new field or a locked relationship; it's guidance for whoever
tunes these two values together, since getting it wrong either speeds up
or slows down the game unintentionally as a side effect of a resolution
change.

---

## 15. Day & Season Boundaries (added — coherence-check pass)

Closes a real gap found during coherence review: "day-end" was referenced
repeatedly across all four other docs (day-end summary, day-end costs,
bathroom end-of-day reset) but never once actually defined — no trigger,
no command, nothing. Built directly on `MealPeriodDefinition` (entity doc
§24a), which already gives the restaurant's operating hours a real shape.

### Day boundary

```
Opening = earliest StartTime among RestaurantConfig.ActiveMealPeriods
Closing = latest EndTime among RestaurantConfig.ActiveMealPeriods
```

A restaurant's day runs from `Opening` to whenever `Closing` is reached
**and** the restaurant has actually cleared out — not a fixed tick count.

### Day-end trigger

```
DayEndEligible =
  CurrentTick's time-of-day >= Closing
  AND Restaurant.CustomerService.ActiveGroups.Count == 0
  AND Restaurant.CustomerService.Queue.Count == 0
```

Both queue and active groups must be empty — closing time stops *new*
arrivals (already true: Arrival is gated by `ActiveMealPeriods`, business
logic §1.1), but a group already queued or seated before closing is still
served out normally (including the existing Mood-based abandonment path)
rather than being cut off.

Checked at the same tick-scheduled cadence as other timed transitions
(§8). On `DayEndEligible == true`, the sim loop dispatches
`EndDayCommand` — not an inline side-effect, per CLAUDE.md's concurrency
rules — which:

1. Appends a `StatisticsSnapshot` via `ISnapshotStore` (architecture doc
   §7.5), including the day's `Costs`/`NetProfit`/`ReputationDelta`
   (entity doc §23).
2. Applies recurring day-end costs to `Restaurant.Cash` (wages, cleaning
   fee, restock, maintenance, utilities — business logic §4).
3. Resets every `Bathroom.CleanlinessScore = 100` — the "end-of-day
   processing" §12 already named but never had a trigger for.
4. Increments `Restaurant.CurrentDayNumber` (entity doc §19).
5. Auto-advances `SimulationTick` directly to next day's `Opening` — no
   player decision needed here, since nothing happens overnight (no
   arrivals, no staff on shift — see the skip-safety note below).
6. Raises `DayEnded` (past-tense domain event, `CLAUDE.md` naming
   convention) — Infrastructure can translate this into a day-end summary
   screen over SignalR.

### Player-triggered skip — `SkipToNextMealPeriodCommand`

For an **intra-day** gap between two of a restaurant's own active periods
(e.g. 15:00–19:00 between Lunch and Dinner in a lunch+dinner-only
preset) — not applicable at the true end of day, which `EndDayCommand`
already auto-advances past. Player-facing, optional convenience — matches
Duarte's "could allow to skip the time, or speed it up" framing, i.e. this
is a nicety, not a forced behavior.

```
SkipEligible = CurrentTick's time-of-day is between two ActiveMealPeriods
               (past one's EndTime, before the next's StartTime)
               AND ActiveGroups.Count == 0 AND Queue.Count == 0
```

Jumps `SimulationTick` directly to the next period's `StartTime`.
**Safe for arrivals; now resolved for Fatigue too — updated, §13a
backport.** No arrivals spawn during a closed gap (period-gated) — safe
by construction. `OffShift` spans a staff member's full daily shift
(entity doc §6/§24a) — a staff member rostered across the gap (e.g.
`["lunch", "dinner"]`, `default_diner`'s own MVP default) does **not** go
`OffShift` during it. Now that `Fatigue` accrual has a real formula
(§13a): `FatigueDelta_per_tick = BaseFatigueAccrualRate` applies purely
from `State != OffShift`, with no dependency on customer/order activity
— so skipping the gap's ticks **does genuinely undercount** that staff
member's `Fatigue` for the skipped stretch, a real (if likely small)
side-effect of this convenience command, not an unknown anymore. Left as
an accepted simplification for MVP rather than a blocker — revisit if
playtesting shows it matters.

### Season (proposed, not locked — deferred to the actual seasonality DLC)

**Revised — coherence-check pass.** A flat `DaysPerSeason` scalar was
proposed here originally, but that bakes in "every season is the same
length" — inconsistent with the string-key preset-data pattern used
everywhere else a variable, DLC-extensible set exists (`TableShape`,
`KitchenStationType`, `MealPeriodDefinition`). Corrected to the same
shape, data only — **the seasonal *effects* (decor synergy, arrival/Mood
modifiers) remain fully undesigned and out of scope here**, same as
before; only the minimal indexing structure `CurrentDayNumber` would need
is defined, so it isn't retrofitted later or built twice:

```
SeasonDefinition (preset data, string-keyed like MealPeriodDefinition):
  SeasonId: string   // e.g. "spring", "summer", "winter", "holiday_week"
  DayCount: int      // independent per season — 7 is Duarte's starting
                      // default, not a fixed rule; a "holiday_week" could
                      // be shorter, a "winter" longer

RestaurantConfig.SeasonCycle: IReadOnlyList<SeasonDefinition>  // ordered, cycles

TotalCycleDays = Sum(SeasonCycle.DayCount)
DayInCycle = CurrentDayNumber % TotalCycleDays
CurrentSeason = SeasonDefinition whose cumulative-day range contains DayInCycle
```

A DLC pack adds a season by inserting one `SeasonDefinition` into the
cycle — no code change, same "genuinely data-only" extensibility already
established for `MealPeriodDefinition` (entity doc §24a). Nothing beyond
this indexing shape is built now.

---

## 16. Doc-Sync Notes (pending — not yet applied to entities/architecture docs)

Items this session introduced or clarified that touch the **entity** or
**architecture** docs but haven't been folded back into them yet (both are
outside this file's edit scope). Apply when those docs are next revised:

- ~~**`DeliverOrderItem` command** — new, introduced in §10 (Plating &
  Delivery). Needs adding wherever the command list eventually gets
  formalized.~~ **Resolved** — `DeliverOrderItemCommand` is listed in
  `CLAUDE.md`'s "Commands Introduced This Session" section.
- **`OrderItem.HeldSinceTick` semantics** — entity doc states this is set
  when `Status → HeldUnderLamp`; §10 clarifies the hold period now explicitly
  **ends at pickup for delivery**, not at arrival at the table. Not a
  contradiction, but the entity doc's field description could note this
  explicitly for clarity.
- **Kitchen "pass point"** — §10's delivery leg needs a real `GridPosition`
  to path from (where a cooked item becomes pickup-ready), and neither
  `KitchenStation` nor the `default_diner_layout` preset currently define
  one. Likely a single fixed point per kitchen layout, added to the presets
  doc/layout JSON — not a schema change to `KitchenStation` itself.

**New items from the concurrency-sweep session — entity doc:** ~~all items
below~~ **Resolved** — the entity doc's §5 (`TableCluster.State` derived,
`RecentJokeCount`/`LastJokeTick`), §9 (`Customer.IsPresentAtTable`/`Gender`/
`HasUsedBathroom`), and §24 (`Bathroom` entity) already contain everything
flagged here, and `CLAUDE.md`'s "Commands Introduced This Session" section
already lists all four new commands. Kept here only as a historical record
of what this session originally flagged — no further action needed.

**New items — architecture doc:** ~~all items below~~ **Resolved** — the
architecture doc's §11 "Concurrency Model" (and §2, §5 updates) now cover
every point that was pending here: the per-`RestaurantId` lock/pipeline
behavior, AutoManager's per-decision sequencing, `IStaffTaskPriorityStrategy`,
`Restaurant.AutoManagerToggles`, the named "check all, then commit all"
convention (also in `CLAUDE.md`), the cross-restaurant tick-scheduling note
in §2, and the lock-fairness standing note. Kept here only as a historical
record of what this session originally flagged — no further action needed.

**New items — presets/config doc:**

- ~~`RestaurantConfig.TicksPerRealSecond` — new field, see §14 of this
  doc.~~ **Resolved** — added to the entity doc's §22 field table
  (coherence-check pass).
- ~~Every preset duration field needs renaming/reauthoring from raw tick
  counts to seconds (e.g. `MenuItem.PrepTicks` → `MenuItem.PrepTimeSeconds`)
  per §14's conversion pattern.~~ **Resolved** — `MenuItem.PrepTicks`
  renamed to `PrepTimeSeconds` in the entity doc's §18 (coherence-check
  pass); this doc's §10 formula updated to derive `PrepTicks` from it.
- ~~New "open, pending playtesting" tunables introduced this session, to
  be added alongside `PatienceDecayBaseline`: `AutoJokeCooldownSeconds`,
  `MoodGapThreshold` (joke targeting), bathroom visit sub-step durations
  (Male/Female × UseFacilities/WashHands), `TableCleaningBaseSeconds`,
  `BathroomCleaningBaseSeconds`, `AlmostDirtyThreshold`/`DirtyThreshold`
  for bathroom cleanliness.~~ **Resolved** — all added to the entity
  doc's §22 field table (coherence-check pass). Naming convention for
  all of these: the preset-authored field takes the `*Seconds` suffix;
  the derived value a formula actually consumes (e.g. `BaseTicks` in
  §13) is computed from it at load time per §14 — the two are never the
  same name.
- Bathroom layout entries needed in `default_diner_layout` — door
  position(s) for Male/Female/Staff bathrooms.
- **Second coherence-check pass:** the "Resolved" claim above was actually
  incomplete — `ShortDelayThreshold`, `MaxAcceptableWaitTicks`, and
  `PlatingTicks` had all been added to the entity doc still carrying
  `Ticks`-style names despite being seconds-authored (or, worse, typed
  `(seconds)` while literally named `Ticks`), violating the same-name rule
  this note claimed was already enforced. Renamed to
  `ShortDelayThresholdSeconds`, `MaxAcceptableWaitSeconds`,
  `PlatingTimeSeconds` (entity doc §22, presets doc, this doc's §10) —
  now actually resolved.
- Also added in this pass: `TipBaseRate`/`TipMoodExponent` (§6, entity
  doc §22) to back the tip formula's previously-unnamed literal
  coefficients, and `PatienceDecayBaseline` wired into the §2 decay
  formula (was defined as a field but never consumed anywhere).
