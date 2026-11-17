# Restaurant Simulator — MVP Preset Content

Companion to `restaurant-simulator-entities.md` and
`restaurant-simulator-architecture.md`. This file is actual game content —
the first `RestaurantConfig` preset and everything it references — not
design reasoning. Values here are the literal source to convert into JSON
under `Api/Presets/` once implementation starts (see architecture doc §9,
`IPresetRepository<T>`). All names are final English — any earlier
Portuguese terms used during design were placeholders for concepts, not
intended in-game text.

Status: structure locked; numeric values are first-pass unless marked otherwise (see Status at the end)

---

## RestaurantConfig — `"default_diner"`

| Field | Value | Notes |
|---|---|---|
| `ConfigId` | `"default_diner"` | |
| `StartingCash` | `5000` | Currency assumed EUR (Portuguese-context project) |
| `GameMinutesPerRealSecond` | `1` | Game pace: 1 in-game minute per real second. **Replaces the stored `MinutesPerTick`** (tick-rate independence pass), which is now derived: `GameMinutesPerRealSecond / TicksPerRealSecond` (= 1 at the default `TicksPerRealSecond`; a 12-hour service ≈ 720 ticks). A separate knob from `TicksPerRealSecond` below (mechanics doc §14) |
| `TicksPerRealSecond` | `1` | 1 tick = 1 real-world second (MVP default, confirmed baseline — see mechanics doc §14). Every duration field below is authored in real-world seconds and converted to ticks via this value at preset-load time; changing simulation speed later is a single-value edit here, nothing else in this file changes |
| `ReputationStart` | `20` | New/unestablished restaurant — not bad, just not built up yet |
| `ActiveMealPeriods` | `"lunch" (12:00–15:00), "dinner" (19:00–00:00)` — no `ArrivalCutoffTime` authored on either, so each defaults to its own `EndTime` (entity doc §24a) | Typical sit-down restaurant hours, matching the "normal restaurant" default — `"breakfast"`/`"afternoon"` are valid `MealPeriodId` values (entity doc §24a) but unused by this preset. No arrivals spawn, no staff clocked in, outside these windows |
| `PatienceDecayBaseline` | *open* | Deliberately left untuned — needs real playtesting once Mood/Patience logic exists. Do not guess a number to fill this in; flag it as pending if asked |
| `BaseFatigueAccrualPerSecond` | `≈ 0.0000231` ⚠ | Per-real-second Fatigue accrual while on-shift (was per tick; numbers unchanged at `TicksPerRealSecond = 1`) (mechanics doc §13a). First-pass default implemented from the intent "reaches ~1.0 by the end of a typical 12-hour shift". **⚠ Unit discrepancy, needs a decision:** `0.0000231 = 1/43,200` is 12 *real-time* hours of ticks, but with `MinutesPerTick = 1` an in-game 12-hour shift is only 720 ticks (`≈ 0.001389`/tick) — see mechanics doc §13a. Not changed here |
| `OvertimeFatigueMultiplier` | `2.0` | Multiplier on the base rate while held past `ShiftEnd` by the coverage rule (entity doc §6/§24a, mechanics doc §13a). Confirmed in chat: overtime is more tiring, not less. First-pass; pending playtesting |
| `QueueWaitMoodDecayPerSecond` | `-0.4` (per real second) | Matches mechanics doc §2's locked default |
| `CourseWaitMoodDecayPerSecond` | `-0.3` (per real second) | Matches mechanics doc §2's locked default |
| `NaturalMoodRecoveryPerSecond` | `0.5` (per real second) | Matches mechanics doc §2's locked default |
| `ProbabilityCheckpointIntervalSeconds` | `3` | Matches mechanics doc §7's locked default |
| `PathReplanMaxWaitSeconds` | `5` | Matches mechanics doc §9's locked default |
| `MaxCallAttempts` | `3` | Matches entity doc §10's locked default |
| `QueueAbandonmentMoodThreshold` | *unset → `15`* | Nullable; defaults to 15 if unset (the Very Low tier boundary, mechanics doc §2). Left unset in this preset — the default is the intended value. The reputation penalty (flat 0.5) is a code constant, not a field |
| `ReinformMoodPenalty` | `3` | First-pass default (low end of the waiter-action-failure range, mechanics doc §2/§10). Nullable/open; pending playtesting |
| `PreOpeningLeadMinutes` | `30` | In-game minutes before Opening that a new restaurant starts at, clamped to the start of day (mechanics doc §15) |
| `BaseArrivalIntervalSeconds` | `15` *(proposed — implementation seed used `120`)* | **Row did not previously exist in this doc.** Tuning finding: `120` is "2 min at MVP tick rate" in *real* seconds, but one real second is one in-game minute, so 120 ticks = two in-game hours between groups (~245 ticks at lunch start with Reputation 20; a 3-hour lunch gets ~1 group). `15` ≈ a group every ~15 in-game minutes at base rate. Pending playtesting |
| `OversizedPartyReputationPenalty` | `0` | Fraction of current reputation lost when an oversize party is turned away (mechanics doc §18) |
| `MaxMergeGapCells` | `1` | "Nearby only" merging; tables up to 1 empty cell apart can be slid together (mechanics doc §18) |
| `TableMoveSecondsPerCell` | `2` *(first-pass; code fallback)* | "Fairly short"; pending playtesting |
| `ServiceFlow` | *nested; defaults* | `ServiceFlowConfig` nested record (timings in seconds, ready/refill/dessert chances, price bias, eating-duration ranges); omitted here = defaults (mechanics doc §17, draft pending approval) |
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
| `BaseBathroomVisitChance` | *open* | Per-checkpoint base probability a present customer decides to go (mechanics doc §12). Pending playtesting |
| `BathroomCooldownSeconds` | *open* | Minimum time between a customer's bathroom trips (mechanics doc §12). Pending playtesting |
| `EatingMealtimeVisitModifier` | *open* | Multiplier reducing bathroom-visit chance while actively eating (mechanics doc §12). Pending playtesting |
| `AlmostDirtyThreshold`, `DirtyThreshold` | `60`, `30` | Two-tier bathroom cleanliness thresholds (mechanics doc §12). First-pass defaults — shape matches §12's "lead time before it costs Mood" intent; values not derived from anything more specific. Pending playtesting |
| `TableCleaningBaseSeconds` | `60` | Base Busser table-cleaning duration (mechanics doc §13). First-pass default. Pending playtesting |
| `BathroomCleaningBaseSeconds` | `180` | Base Busser bathroom-cleaning duration (mechanics doc §13). First-pass default. Pending playtesting |
| `ComplaintCategoryWeights` | *open* | Base severity weight per `ComplaintCategory` (entity doc §11). Pending playtesting |
| `InspectionDurationSeconds` | *open* | Authored duration of the Inspection grading pass, converted to ticks at load (entity doc §21, §22). Pending playtesting |

---

## Table Layout — `"default_diner_layout"`

Kept separate from the config per the entity doc's own reasoning
(layouts are reusable across multiple configs).

**Tables (revised — table movement, owner decision):**
- 6× standalone `standard_2top`, in two rows of three: row y=0 (T1, T2, T5) and
  row y=2 (T3, T4, T6), columns x=0, 2, 4 — a 1-cell gap between neighbours,
  which `MaxMergeGapCells = 1` lets staff slide together at runtime
  (mechanics doc §18). *(Assumes 1×1-cell table footprints — hitbox sizes are not authored in any doc; status doc D-11.)* **The pre-merged T5+T6 4-top is removed.**

**Total:** 6 physical `Table` entities → 6 `TableCluster`s, total capacity 12
covers; max party size 6 (3 tables in a row, merged on demand). Replaces the
earlier "4 standalone + 1 pre-merged 4-top → 5 clusters" layout.

**Kitchen stations:**
- 1× `grill`
- 1× `fryer`
- 1× `cold_prep`
- 1× `dessert_station`

**Station-type and heat-lamp values — added, implementation-phase backport.**
This section gave station *counts* only; it never gave `Capacity` or
`SpeedMultiplier` per `KitchenStationType`, and never gave
`HeatLamp.QualityDecayRatePerSecond` (only `SlotCount: 3`) — and, unlike other
unset values here, these weren't even marked "open". First-pass proposed
defaults now used (all pending real playtesting):
- `grill`, `fryer`, `dessert_station`: `Capacity = 2`; `cold_prep`:
  `Capacity = 1`
- `SpeedMultiplier = 1.0` for every station type
- `HeatLamp.QualityDecayRatePerSecond = 1.0`

**Passive pickup fixtures:**
- 1× `Freezer` — placed adjacent to `cold_prep` (drinks/cold items
  cluster spatially). Implements `IPlaceable` (real grid position +
  hitbox) even though it carries no stock/capacity logic yet — that
  arrives with Inventory DLC. Placed now so kitchen pathfinding has a
  real destination from day one rather than retrofitting one later.

**Heat lamp:**
- 1× `HeatLamp`, `SlotCount: 3`, `QualityDecayRatePerSecond: 1.0` (first-pass — see above)

**Bathrooms — added in concurrency-sweep session:**
- 1× `Bathroom` (Male), door position TBD at actual layout-authoring time
- 1× `Bathroom` (Female), door position TBD
- 1× `Bathroom` (Unisex — staff), door position TBD
- Per entity doc §24: MVP scope is "doors only" — each is a single door
  tile with one adjacent standing/queue slot, no interior room object.
  `Capacity`, starting `CleanlinessScore`, and `MaxConcurrentCleaners`
  (entity doc §24, new — coherence-check pass) values are open, pending
  playtesting (see mechanics doc §12–13) — not guessed here.

**Coordinates — first-pass values (backported).** Authored in C# seeds
(`DefaultDinerLayoutSeed.cs`, `DefaultDinerKitchenLayoutSeed.cs`), not yet
JSON files. The first seeds overlapped (kitchen stations sat in the same cells
as tables, freezer at (2,1)); fixed by giving the kitchen its own band and
leaving row y = 3 as a walkway:
- Dining tables (each is also that table's `HomePosition`, entities §3):
  T1 (0,0), T2 (2,0), T5 (4,0); T3 (0,2), T4 (2,2), T6 (4,2).
- Kitchen: grill (0,4), fryer (1,4), cold_prep (2,4), dessert_station (3,4),
  heat lamp (4,4), freezer (2,5) (adjacent to cold_prep).
- Bathrooms: x = 10, y = 0–2 (door tiles).
- The delivery-leg pass point (mechanics doc §16) is still undefined.

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

Sized for 6 tables / 6 clusters / max 12 covers.

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
15:00–19:00 gap — the owner's explicit design ("working non-stop, ignoring
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

| Name | Category | Station/Pickup | PrepTimeSeconds | DefaultPrice |
|---|---|---|---|---|
| Water | Adult | Freezer (pickup) | 2 | €1.50 |
| Soda | Adult | Freezer (pickup) | 2 | €2.00 |
| House Wine (glass) | Adult | Freezer (pickup) | 2 | €3.00 |
| Orange Juice | Kids | Freezer (pickup) | 2 | €2.00 |

### Mains (EntryMain phase)

| Name | Category | Station | PrepTimeSeconds | DefaultPrice |
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

| Name | Category | Station | PrepTimeSeconds | DefaultPrice |
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
- `BaseFatigueAccrualPerSecond`, `OvertimeFatigueMultiplier` (Phase 5 of the
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
  `HeatLamp.QualityDecayRatePerSecond` — first-pass defaults set (Layout
  section above); previously missing entirely, not even marked open
- `ReinformMoodPenalty` — first-pass default `3` (mechanics doc §2/§10)
- `QueueAbandonmentMoodThreshold` — nullable, defaults to `15` if unset
  (mechanics doc §2)
- `BaseArrivalIntervalSeconds` — `15` is a **proposal**, not yet confirmed
  (replaces the `120` seed value; see the row above)
- `TableMoveSecondsPerCell`, `OversizedPartyReputationPenalty` — first-pass
  defaults (`2`, `0`); `MaxMergeGapCells = 1` is the owner's "nearby only" call
- `ServiceFlow` values — approved defaults live in code; the full draft is not
  yet merged into the mechanics doc (§17)

None of these block implementation starting — they're tunable constants,
not structural gaps. Where a first-pass default is noted above it is
an implementation-phase starting value, not a playtested one; for any item
with no value listed, do not guess a number — flag it as pending if asked.
