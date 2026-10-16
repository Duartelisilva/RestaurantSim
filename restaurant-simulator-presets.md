# Restaurant Simulator — MVP Preset Content

Companion to `restaurant-simulator-entities.md` and
`restaurant-simulator-architecture.md`. This file is actual game content —
the first `RestaurantConfig` preset and everything it references — not
design reasoning. Values here are the literal source to convert into JSON
under `Api/Presets/` once implementation starts (see architecture doc §9,
`IPresetRepository<T>`). All names are final English — any earlier
Portuguese terms used during design were placeholders for concepts, not
intended in-game text.

Status: 🔒 Locked

---

## RestaurantConfig — `"default_diner"`

| Field | Value | Notes |
|---|---|---|
| `ConfigId` | `"default_diner"` | |
| `StartingCash` | `5000` | Currency assumed EUR (Portuguese-context project) |
| `MinutesPerTick` | `1` | 1 tick = 1 simulated **in-game minute** — a separate knob from `TicksPerRealSecond` below (mechanics doc §14); a 12-hour service ≈ 720 ticks |
| `TicksPerRealSecond` | `1` | **Added in concurrency-sweep session.** 1 tick = 1 real-world second (MVP default, confirmed baseline — see mechanics doc §14). Every duration field below is authored in real-world seconds and converted to ticks via this value at preset-load time; changing simulation speed later is a single-value edit here, nothing else in this file changes |
| `ReputationStart` | `20` | New/unestablished restaurant — not bad, just not built up yet |
| `ActiveMealPeriods` | `"lunch" (12:00–15:00), "dinner" (19:00–00:00)` — no `ArrivalCutoffTime` authored on either, so each defaults to its own `EndTime` (entity doc §24a) | **Added — coherence-check pass.** Typical sit-down restaurant hours, matching the "normal restaurant" default — `"breakfast"`/`"afternoon"` are valid `MealPeriodId` values (entity doc §24a) but unused by this preset. No arrivals spawn, no staff clocked in, outside these windows |
| `PatienceDecayBaseline` | *open* | Deliberately left untuned — needs real playtesting once Mood/Patience logic exists. Do not guess a number to fill this in; flag it as pending if asked |
| `BaseFatigueAccrualRate` | `≈ 0.0000231` ⚠ | **Added — backported from implementation-phase decisions.** Per-tick Fatigue accrual while on-shift (mechanics doc §13a). First-pass default implemented from the intent "reaches ~1.0 by the end of a typical 12-hour shift". **⚠ Unit discrepancy, needs a decision:** `0.0000231 = 1/43,200` is 12 *real-time* hours of ticks, but with `MinutesPerTick = 1` an in-game 12-hour shift is only 720 ticks (`≈ 0.001389`/tick) — see mechanics doc §13a. Not changed here |
| `OvertimeFatigueMultiplier` | `2.0` | **Added — backported from implementation-phase decisions.** Multiplier on the base rate while held past `ShiftEnd` by the coverage rule (entity doc §6/§24a, mechanics doc §13a). Confirmed in chat: overtime is more tiring, not less. First-pass; pending playtesting |
| `QueueWaitMoodDecayRate` | `-0.4` | **Added — coherence-check pass.** Matches mechanics doc §2's locked default |
| `CourseWaitMoodDecayRate` | `-0.3` | **Added — coherence-check pass.** Matches mechanics doc §2's locked default |
| `NaturalMoodRecoveryRate` | `0.5` | **Added — coherence-check pass.** Matches mechanics doc §2's locked default |
| `ProbabilityCheckpointIntervalSeconds` | `3` | **Added — coherence-check pass.** Matches mechanics doc §7's locked default |
| `PathReplanMaxWaitSeconds` | `5` | **Added — coherence-check pass.** Matches mechanics doc §9's locked default |
| `MaxCallAttempts` | `3` | **Added — coherence-check pass.** Matches entity doc §10's locked default |
| `QueueAbandonmentMoodThreshold` | *unset → `15`* | **Added — implementation-phase backport.** Nullable; defaults to 15 if unset (the Very Low tier boundary, mechanics doc §2). Left unset in this preset — the default is the intended value. The reputation penalty (flat 0.5) is a code constant, not a field |
| `ReinformMoodPenalty` | `3` | **Added — implementation-phase backport.** First-pass default (low end of the waiter-action-failure range, mechanics doc §2/§10). Nullable/open; pending playtesting |
| `EventModuleIds` | `[]` | No DLC modules active by default. Populate as modules are actually built (Phase 8 of the build order) — e.g. `ComplaintsBookModule` (entity doc §11), left off since it's a Portugal-specific mechanic, not universal |
| `TableLayoutId` | `"default_diner_layout"` | See below |
| `StaffRosterSeed` | see below | |
| `MenuItemIds` | all 16 items below | |
| `ShortDelayThresholdSeconds` | *open* | Heat-lamp-vs-reinform base threshold, converted to `ShortDelayThresholdTicks` at load (mechanics doc §10, §14). Pending playtesting |
| `MaxAcceptableWaitSeconds` | *open* | `WaitPressure` denominator in the heat-lamp decision, converted to `MaxAcceptableWaitTicks` at load (mechanics doc §10, §14). Pending playtesting |
| `BaseHighQualityChance` | *open* | Base coefficient in the `IsHighQuality` roll (mechanics doc §10). Pending playtesting |
| `BaseEventChance` | *open* | Base coefficient in the `FoodQualityEvent` roll (mechanics doc §10) — starting default `0.03` proposed in mechanics doc, not yet locked |
| `PlatingTimeSeconds` | *open* | Flat cook→pass-point transfer time, converted to `PlatingTicks` at load (mechanics doc §10, §14). Pending playtesting |
| `TipBaseRate` | `50` | Base coefficient in the tip formula (mechanics doc §6) |
| `TipMoodExponent` | `0.75` | Exponent applied to `Mood/100` in the tip formula (mechanics doc §6) |
| `AutoJokeCooldownSeconds` | `60` | Per-table autonomous-joke cooldown (mechanics doc §11). First-pass default (implementation phase; no doc-cited example existed). Pending playtesting |
| `MoodGapThreshold` | `20` | Mood-gap that triggers person-targeted vs. table-targeted jokes (mechanics doc §11). First-pass default. Pending playtesting |
| `FacilitiesTimeSecondsMale`, `FacilitiesTimeSecondsFemale` | *open* | Gendered `UseFacilities` sub-step duration (mechanics doc §12). Pending playtesting |
| `WashHandsTimeSecondsMale`, `WashHandsTimeSecondsFemale` | *open* | Gendered `WashHands` sub-step duration (mechanics doc §12). Pending playtesting |
| `BaseBathroomVisitChance` | *open* | **Added — coherence-check pass.** Per-checkpoint base probability a present customer decides to go (mechanics doc §12). Pending playtesting |
| `BathroomCooldownSeconds` | *open* | **Added — coherence-check pass.** Minimum time between a customer's bathroom trips (mechanics doc §12). Pending playtesting |
| `EatingMealtimeVisitModifier` | *open* | **Added — coherence-check pass.** Multiplier reducing bathroom-visit chance while actively eating (mechanics doc §12). Pending playtesting |
| `AlmostDirtyThreshold`, `DirtyThreshold` | `60`, `30` | Two-tier bathroom cleanliness thresholds (mechanics doc §12). First-pass defaults — shape matches §12's "lead time before it costs Mood" intent; values not derived from anything more specific. Pending playtesting |
| `TableCleaningBaseSeconds` | `60` | Base Busser table-cleaning duration (mechanics doc §13). First-pass default. Pending playtesting |
| `BathroomCleaningBaseSeconds` | `180` | Base Busser bathroom-cleaning duration (mechanics doc §13). First-pass default. Pending playtesting |
| `ComplaintCategoryWeights` | *open* | Base severity weight per `ComplaintCategory` (entity doc §11). Pending playtesting |
| `InspectionDurationSeconds` | *open* | Authored duration of the Inspection grading pass, converted to ticks at load (entity doc §21, §22). Pending playtesting |

---

## Table Layout — `"default_diner_layout"`

Kept separate from the config per the entity doc's own reasoning
(layouts are reusable across multiple configs).

**Tables:**
- 4× standalone `standard_2top`
- 1× pre-merged 4-top — 2 additional `standard_2top` tables, placed
  adjacent and pre-merged into one `TableCluster` (`Capacity: 4`) at
  layout-load time. This uses the same merge mechanism already defined
  for live play (entity doc §5) — just authored as already-merged rather
  than merged during a session. No new `TableShape` needed.

**Total:** 6 physical `Table` entities → 5 `TableCluster`s, max capacity
12 covers.

**Kitchen stations:**
- 1× `grill`
- 1× `fryer`
- 1× `cold_prep`
- 1× `dessert_station`

**Station-type and heat-lamp values — added, implementation-phase backport.**
This section gave station *counts* only; it never gave `Capacity` or
`SpeedMultiplier` per `KitchenStationType`, and never gave
`HeatLamp.QualityDecayRatePerTick` (only `SlotCount: 3`) — and, unlike other
unset values here, these weren't even marked "open". First-pass proposed
defaults now used (all pending real playtesting):
- `grill`, `fryer`, `dessert_station`: `Capacity = 2`; `cold_prep`:
  `Capacity = 1`
- `SpeedMultiplier = 1.0` for every station type
- `HeatLamp.QualityDecayRatePerTick = 1.0`

**Passive pickup fixtures:**
- 1× `Freezer` — placed adjacent to `cold_prep` (drinks/cold items
  cluster spatially). Implements `IPlaceable` (real grid position +
  hitbox) even though it carries no stock/capacity logic yet — that
  arrives with Inventory DLC. Placed now so kitchen pathfinding has a
  real destination from day one rather than retrofitting one later.

**Heat lamp:**
- 1× `HeatLamp`, `SlotCount: 3`, `QualityDecayRatePerTick: 1.0` (first-pass — see above)

**Bathrooms — added in concurrency-sweep session:**
- 1× `Bathroom` (Male), door position TBD at actual layout-authoring time
- 1× `Bathroom` (Female), door position TBD
- 1× `Bathroom` (Unisex — staff), door position TBD
- Per entity doc §24: MVP scope is "doors only" — each is a single door
  tile with one adjacent standing/queue slot, no interior room object.
  `Capacity`, starting `CleanlinessScore`, and `MaxConcurrentCleaners`
  (entity doc §24, new — coherence-check pass) values are open, pending
  playtesting (see mechanics doc §12–13) — not guessed here.

**Coordinates — not authored in this doc.** This section describes counts
and rough placement only ("adjacent to `cold_prep`", "door position TBD").
The actual table positions and bathroom door positions were first authored in
the implementation phase as first-pass content in the `default_diner_layout`
JSON; they aren't specified here.

**Flagged for future (not built, not placed — allow for it, don't build it):**
- A soda-fountain-style drink dispenser ("pressure drinks," e.g. draft
  soda from a machine rather than bottled/canned). This is a genuinely
  different case from the passive `Freezer` pickup — it's an *active*
  piece of equipment, so it fits the existing `KitchenStationType`
  pattern (like `grill`/`fryer`) rather than `PickupSourceType`. When
  wanted: add a new `KitchenStationType` preset entry (e.g.
  `"soda_fountain"`) and menu items referencing it. Zero schema changes,
  zero new code — flagged here only so the intent isn't lost.

---

## Staff Roster Seed

Sized for 6 tables / 5 clusters / max 12 covers.

| Role | Count |
|---|---|
| Host | 1 |
| Waiter | 2 |
| HeadChef | 1 |
| Cook | 2 |
| Busser | 1 |

**Total: 7 staff.** Enough to distribute section assignments across 2
waiters, and gives the "no HeadChef present → kitchen-wide quality
malus" rule (business logic doc §2) something real to guard against.

**`AssignedMealPeriods`** — **added, coherence-check pass:** all 7 staff
are assigned `["lunch", "dinner"]` (both of `default_diner`'s
`ActiveMealPeriods` above) for MVP simplicity — this roster is too small
to meaningfully split into separate lunch-only/dinner-only shifts. **Given
the corrected shift-span behavior (entity doc §6/§24a):** this means one
continuous 12:00–00:00 shift with no `Fatigue` reset during the
15:00–19:00 gap — Duarte's explicit design ("working non-stop, ignoring
the breaks"), not an unintended side effect. A larger roster splitting
shifts per period is a natural post-MVP tuning change, not a structural
one — the field already supports it.

**Seeded-roster content — first-pass, implementation-phase backport (not
previously specified anywhere):** staff are named by a simple scheme
(`"Waiter 1"`, `"Cook 2"`) — no name-generation system exists in any doc;
all starting stats are baseline `0.5` (entity doc §7's own stated average)
with `Fatigue = 0` — no random-rolled hiring pool (explicit DLC, business
logic §6); and seeded staff start `State = Idle` rather than `OffShift`, a
flagged simplification (entity doc §24a).

---

## Menu — `"default_diner"` catalog (16 items)

Covers every station in the layout (grill, fryer, cold_prep,
dessert_station, freezer pickup) plus the Kids category. Prices are
rough placeholder-realistic estimates — tune freely, this is not
researched against real menus.

### Beverages (Drinks phase)

| Name | Category | Station/Pickup | PrepTimeSeconds | Price |
|---|---|---|---|---|
| Water | Adult | Freezer (pickup) | 2 | €1.50 |
| Soda | Adult | Freezer (pickup) | 2 | €2.00 |
| House Wine (glass) | Adult | Freezer (pickup) | 2 | €3.00 |
| Orange Juice | Kids | Freezer (pickup) | 2 | €2.00 |

### Mains (EntryMain phase)

| Name | Category | Station | PrepTimeSeconds | Price |
|---|---|---|---|---|
| Grilled Steak | Adult | grill | 14 | €12.50 |
| Grilled Chicken | Adult | grill | 12 | €10.00 |
| Grilled Fish | Adult | grill | 13 | €13.00 |
| Steak & Egg with Fries | Adult | grill | 10 | €9.50 |
| Stuffed Toasted Sandwich | Adult | fryer | 11 | €11.00 |
| Shredded Cod & Fried Potato | Adult | fryer | 9 | €10.50 |
| Caesar Salad | Adult | cold_prep | 6 | €8.00 |
| Chicken Nuggets | Kids | fryer | 7 | €6.00 |
| Pasta with Sauce | Kids | grill | 8 | €5.50 |

### Desserts (Dessert phase)

| Name | Category | Station | PrepTimeSeconds | Price |
|---|---|---|---|---|
| Custard Tart | Adult | dessert_station | 3 | €2.00 |
| Chocolate Mousse | Adult | dessert_station | 4 | €3.50 |
| Ice Cream | Kids | dessert_station | 3 | €2.50 |

### Balance notes (observations, not corrections — nothing here blocks implementation)

- Adult mains cluster tightly (9–14 seconds), which keeps the course-sync
  offset (business logic doc §2) small enough for the 3-slot heat lamp
  to meaningfully absorb most delays, rather than forcing "re-inform"
  constantly. (At the MVP default `TicksPerRealSecond = 1`, these numbers
  are numerically identical to ticks — they'll diverge only if that value
  ever changes.)
- Kids items are consistently faster (2–8 seconds) and capped at €6.00,
  matching business logic doc §1.4.
- `grill` is shared by both adult and kids mains (Grilled Steak at 14
  vs. Pasta with Sauce at 8) — this is the layout's busiest, most
  contended station, and a natural stress-test of station contention +
  sync logic. Expect kitchen bottlenecks to surface here first in
  playtesting.
- `cold_prep` currently has only one item (Caesar Salad) — thin but
  intentional for MVP; adding a second cold item later is pure data,
  zero code change.

---

## Status

All values above are locked for MVP except the following, deliberately
left open pending real playtesting rather than guessed:

- `PatienceDecayBaseline` (Phase 4 of the build order) — now wired into the
  Mood decay formula (mechanics doc §2); defaults to `1.0` if left unset
- `BaseFatigueAccrualRate`, `OvertimeFatigueMultiplier` (Phase 5 of the
  build order) — staff Fatigue accrual, previously had no formula at all
  (mechanics doc §13a) — **first-pass defaults now set; ⚠ unit discrepancy
  flagged there**
- `ShortDelayThresholdSeconds`, `MaxAcceptableWaitSeconds` —
  heat-lamp-vs-reinform decision (mechanics doc §10)
- `BaseHighQualityChance`, `BaseEventChance` — food quality rolls
  (mechanics doc §10)
- `PlatingTimeSeconds` — cook→pass-point transfer time (mechanics doc §10)
- `AutoJokeCooldownSeconds`, `MoodGapThreshold` — joke targeting/cooldown
  (mechanics doc §11) — **first-pass defaults now set (table above)**
- Bathroom visit sub-step durations — `UseFacilities`/`WashHands`,
  Male/Female (mechanics doc §12)
- `BaseBathroomVisitChance`, `BathroomCooldownSeconds`,
  `EatingMealtimeVisitModifier` — bathroom-visit trigger roll (mechanics
  doc §12, added in coherence-check pass)
- `AlmostDirtyThreshold`, `DirtyThreshold` — bathroom cleanliness
  (mechanics doc §12) — **first-pass defaults now set (table above)**
- `TableCleaningBaseSeconds`, `BathroomCleaningBaseSeconds` — Busser
  duration tasks (mechanics doc §13) — **first-pass defaults now set (table above)**
- `ComplaintCategoryWeights` — complaint severity base weights per
  category (entity doc §11)
- `InspectionDurationSeconds` — authored duration of the Inspection
  grading pass (entity doc §21, §22)
- `Bathroom.Capacity`, starting `CleanlinessScore`, `MaxConcurrentCleaners`
  per bathroom instance (this file, Table Layout section above)
- `KitchenStationType.Capacity`/`SpeedMultiplier` per station type and
  `HeatLamp.QualityDecayRatePerTick` — first-pass defaults set (Layout
  section above); previously missing entirely, not even marked open
- `ReinformMoodPenalty` — first-pass default `3` (mechanics doc §2/§10)
- `QueueAbandonmentMoodThreshold` — nullable, defaults to `15` if unset
  (mechanics doc §2)

None of these block implementation starting — they're tunable constants,
not structural gaps. Where a first-pass default is noted above it is
an implementation-phase starting value, not a playtested one; for any item
with no value listed, do not guess a number — flag it as pending if asked.
