# Restaurant Simulator — Business Logic

Design reference inspired by RollerCoaster Tycoon's management depth. Portuguese context (e.g. "livro de reclamações"). Core architectural constraint: **fully data-driven and preset-agnostic** — any restaurant configuration loads without code changes. Secondary goal: build this as a **reusable simulation engine** others can build on, with a full **auto-manager AI layer** capable of making every manual decision optimally (design validation + accessibility feature).

Game is **endless simulation**, not scenario/win-loss based. Any number of `RestaurantPreset`s can be authored as pure data. Sandbox/dev mode (free money, instant unlocks) is a low-priority future toggle, not core scope.

---

## MVP Scope (locked)

Core dine-in loop only: **Arrival → Queue → Seating → Ordering → Kitchen → Eating → Paying → Leaving**, with basic staff roles (Waiter, Host, Cook, HeadChef, Busser) and basic economy (revenue, costs, day-end summary).

Everything else below is post-MVP ("DLC" — makes the experience more complete, not literal paid DLC).

---

## 1. Customer Lifecycle

### 1.1 Arrival
- **Gated by meal period, added — coherence-check pass:** groups only spawn during one of `RestaurantConfig.ActiveMealPeriods`' windows (entity doc §24a) — a restaurant with no active period covering the current time has no arrivals at all, not just a slower rate. Within an active period, spawn rate is further weighted by time-of-day (e.g. the middle of a Lunch window vs. its edges — resolves what "lunch/dinner rush" meant before periods had real boundaries).
- Groups spawn at random intervals, weighted by time-of-day (lunch/dinner rush = shorter intervals).
- Spawn rate = base rate × **reputation** modifier × **queue pressure** modifier. Reputation (0–100 stat) raised by upgrades/good reviews, lowered by bad experiences; higher reputation → more frequent + bigger groups. Queue pressure: once queue length crosses a threshold, spawn interval increases (self-balancing negative feedback loop).
- Group size: weighted random 1–6, skewed toward 2.
- Group profile per group: `PatienceLevel`, `SpendingTendency` (budget/normal/generous), `IsRegular` (regulars = higher patience, possibly faster seating).
- **Per-person `Mood`** (0–100 float, dynamic): the single emotional state, replacing what would otherwise be two separate variables (agitation + goodwill). Starts at a base value from the profile (~40–60, or higher for regulars/generous types). Everything customer-facing reads from this one number:
  - High Mood → calm, may compliment, better tip
  - Mid Mood → polite ask if something's delayed (calmest adult in group asks)
  - Low Mood → complains, sends food back
  - Very low Mood → loud complaint, walkout, complaints-book filing
  - Tip / final outcome = Mood sampled at payment time (no separate Satisfaction variable needed)
  - **Waiter check-in action**: waiter can proactively visit a table mid-meal to ask if they're enjoying the food — a `WaiterAction` (charisma-gated like Joke/SmallTalk) that raises Mood on success.
- **`PatienceLevel`**: not a threshold-trigger, but a **modifier trait** on how *fast* Mood drops under waiting/negative events and how fast it recovers under positive ones. Low patience = steep drops, slow recovery. High patience = shallow drops, fast recovery.
- **`Instability`**: a **fixed trait, does not change over the visit** (unlike Mood). Acts as a severity/threshold multiplier on Mood-driven complaint rolls — given the *same* current Mood, higher Instability makes loud/severe reactions (loud complaint, sending food back, complaints-book filing) more likely, while lower Instability tends toward quiet suffering (low Mood persists and still hurts the tip, but rarely escalates). `PatienceLevel` governs *how fast Mood changes*; `Instability` governs *how a given Mood level gets acted on*.
- **`SusceptibilityToGroup`**: per-person pull toward the group's Mood.
  - **Intra-group spread**: Mood drifts toward group average each tick, weighted by role (e.g. parent has stronger calming influence on kids). An already-low-Mood group can *amplify* downward instead of calm — tipping point mechanic.
  - **Inter-group spread**: much weaker pull; only triggers on high-visibility events (shouting, walkouts) and affects nearby groups' Mood, not directly individuals. Chance-based (does it spread) × magnitude-based (how much) — a double roll.
- **Reservations**: `Reservation` entity (group profile pre-generated at booking, `ScheduledTime`, `PartySize`). Reserved parties skip/priority the queue at their time; late beyond grace window → host can release table (reputation/trust penalty).

### 1.2 Queue
- Max queue length (tunable, tied to layout/size); beyond max, groups turned away immediately (reputation hit scaled by current reputation).
- `TimeInQueue` tracked; Mood drops with wait, rate/recovery shaped by `PatienceLevel`.
- **Complaint escalation by Mood tier, severity shaped by `Instability`**:
  - Low Mood → chance to complain loudly (visible event, can spread to nearby groups via inter-group roll) — likelihood/severity scaled up by the person's `Instability`.
  - Mid Mood → calmest adult in the group politely asks status (applies to both queue wait and food wait). This is a `WaiterAction`-triggerable moment — waiter can soothe (raise Mood) or worsen it (lower Mood further) based on `WaiterCharisma`.
- Abandonment: if group's average Mood drops below a threshold while queued, they leave (reputation penalty, worse if witnessed — same chance × magnitude roll as above).
- **Host seating logic**: greedy best-fit matching, not strict FIFO — e.g. skip a 4-person family to seat a 2-person couple if only a 2-top is free.

### 1.3 Seating
- The occupiable seating unit (`TableCluster` in the entity doc — one or more merged physical `Table`s) has a derived state: Free/Reserved/Occupied/NeedsCleaning.
- Host assigns smallest table that fits the group (preserves larger tables for larger groups).
- After a party leaves, each physical `Table` in the cluster individually becomes dirty; a Busser must clean each one before that table is usable again (bottleneck, ties to staff assignment). A merged cluster with some tables already clean can be split and those tables used/merged separately without waiting for the whole cluster.
- Section assignment balances waiter workload (waiter only serves their section).

### 1.4 Ordering
- Fixed sequence: **Seated → Beverages → Main → Dessert/Coffee → Bill → Goodbye.**
- Delay before waiter arrives at each phase depends on waiter's load/section busyness — same polite-ask/complain wait mechanic as Queue.
- Orders taken **all at once per phase** per table (one person's request "opens the window" for the whole table). Beverages can be requested individually mid-meal.
- Order influenced by `SpendingTendency`.
- Waiter can upsell (active, charisma-gated): success raises order value/tip potential; failure (pushy) risks mild Mood drop.
- Separate **Kids menu** (`MenuItem.Category` = Adult/Kids), kids items generally faster prep + lower price ceiling.
- **Meal-period restriction, added — coherence-check pass:** `MenuItem.AvailableMealPeriods` (entity doc §18, §24a) gates which items can be ordered during the restaurant's current active period — same enforcement mechanism as kids-menu (command validation, not a stored runtime flag). `default_diner`'s menu is available uniformly across both its active periods for MVP (presets doc) — no item is currently period-restricted, but the mechanism exists for a future period-specific item.

### 1.5 Kitchen (see Section 2 for detail)
- Course-sync scheduling so a table's items finish together despite different prep times.

### 1.55 Bathroom
- **Customer bathroom**: gendered facilities (Male/Female — see entity doc §24's `Bathroom`), each with a capacity and a FIFO queue. A customer leaving the table to use one is unavailable (queueing or inside) for the whole duration, not just while actively using it — see mechanics doc §12 for exact visit composition (an optional "use facilities" step plus mandatory hand-washing, both gendered durations) and the cleanliness-driven Mood delta on return.
- **Staff bathroom**: separate, unisex facility for staff, used periodically (hygiene/break need) — ties into fatigue/hand-washing realism, minor throughput consideration (staff briefly unavailable). No Mood interaction — purely an availability blocker, unlike the customer bathrooms.
- **Waiter response rule**: a customer leaving the table for the bathroom does **not** trigger the waiter to approach automatically (unlike arriving at a new order phase, e.g. Main/Dessert). The waiter only approaches when **called** by the table — e.g. to request drinks or the bill.
- **Group-presence rule for calls**: a table can only request a **group-wide** order phase (e.g. Main course) when the **entire group is present** at the table (`Customer.IsPresentAtTable == true` for every member). A lone member can't call the waiter for a group decision while someone else is away (bathroom, etc.) — mirrors real behavior. Individual asks (e.g. one person's drink) aren't blocked by this rule. The same presence flag also excludes absent members from jokes (§1.6) and freezes their Mood while away — see mechanics doc §12.
- **Cleanliness**: bathrooms degrade by chance on each use, not a flat timer; a Busser cleans them once eligible, restoring cleanliness gradually over time (same duration-task mechanism as table cleaning). See mechanics doc §12–13.

### 1.6 Eating
- Each course consumed over a duration (randomized per person, food-type modifier).
- Mood **recovers naturally while seated** (relaxation), recovery rate shaped by `PatienceLevel` and modified by ambiance/comfort stats — unless food is late/wrong/cold, which drops it instead.
- **Food quality events** (rare): hair in food, wrong temp, undercooked. Complaint probability gated by current Mood/`PatienceLevel`, severity/likelihood of speaking up further shaped by `Instability` — a low-Mood, low-Instability customer may still suffer quietly, while a low-Mood, high-Instability customer complains loudly or sends it back.
- Waiter active interactions (Joke, SmallTalk, Recommendation, Check-in): outcome = roll `WaiterCharisma` vs customer's current Mood. Success raises Mood + bonus tip chance; failure lowers Mood (bad joke lands worse on an already-low-Mood customer). Jokes specifically can also be triggered autonomously by AutoManager (when the waiter-action strategy is enabled) — staff notice a distressed table and joke on their own, with success/failure applying to the whole table at once, scaled per-person by each member's own Mood, and weighted toward whoever was actually addressed. Repeated jokes at the same table lose effectiveness (`RecentJokeCount`-driven fatigue) and a failed joke on a fatigued table lands worse. Full mechanic in mechanics doc §11; absent members (bathroom, etc.) are excluded entirely, per §1.55.

### 1.7 Paying / Leaving
- `TipPercentage = TipBaseRate × (Mood(at payment)/100)^TipMoodExponent × SpendingTendency modifier`,
  applied against the order total — see mechanics doc §6 for the exact
  formula and config fields.
- Tips feed **staff morale** (not restaurant revenue directly) — see Section 4.
- Final Mood/feedback logged for stats and potential complaints-book filing (see Section 5).

---

## 2. Kitchen

- **Order queue**: incoming tickets per table, each ticket = list of items with individual prep times.
- **Course-sync scheduling**: for a multi-item ticket, kitchen computes start-time offsets so items finish together (e.g. start a 10-min dish first, delay a 5-min dish's start by 5 min).
- **Station/resource model**: kitchen has stations (grill, fryer, cold prep, dessert) with limited capacity each; a dish needs its station free to start — station contention can delay sync. Contended items queue oldest-ticket-first, not raw arrival order (see mechanics doc §10).
- **Layout**: stations are placeable entities on a **grid**, each with a **hitbox** for collision (so equipment/staff don't overlap). MVP ships one fixed layout, but layout is data from day one — a later "move mode" is a UI unlock, not a rearchitecture.
- **Heat lamp vs re-inform decision** (evaluated when sync is threatened):
  - Short delay (item nearly ready) → hold finished item under heat lamp; quality decays over hold time; served together.
  - Long delay (e.g. item needs restart/burnt) → waiter informs table, ready items served immediately, table eats asynchronously.
  - Decision also weighted by how long the table has already waited — an already-impatient table skews toward "serve now" even for shorter delays.
- **Cook stats**: `Speed`, `ErrorRate` (wrong item, undercooked), fatigue increases both over a shift.
- **Head Chef** (singleton — max 1 active): higher stat floor than regular cooks, applies a **kitchen-wide quality multiplier** while present, higher wage. **No Head Chef assigned → substantial global food quality malus** (not just "no bonus").

---

## 3. Staff

**Class hierarchy**: `FrontOfHouseStaff` (base) → `Waiter`, `Host`. Plus `Cook` → `HeadChef` (singleton). `Busser` (table cleaning/turnover, separate role). Cleaning (facility) is abstracted as a flat/scaled `DailyExpense`, not a simulated worker.

**Shared base attributes**: `Speed`, `Fatigue` (rises over shift, decays on break, degrades performance), `ErrorRate` (base, worsened by fatigue), `Charisma` (soothing/joke/upsell success), `Experience`/Level. **"Shift" is now a real, defined boundary — added, coherence-check pass:** a staff member's `AssignedMealPeriods` (entity doc §6, §24a) define when they're clocked in (`State != OffShift`); `Fatigue` rises only during that window and resets on clock-out. Previously referenced repeatedly but never actually bounded by anything.

**Role-specific**:
- **Waiter**: assigned tables via `TableCluster.AssignedWaiterId` (dynamic section balancing, not a static `Section` field — see entity doc §5); handles Ordering/Eating/Bill phases; active actions (joke, soothe, upsell, check-in) gated by Charisma vs current Mood roll.
- **Host**: seating logic + reservation management; Charisma affects handling of agitated/queued groups.
- **Cook**: `Station` specialization; Speed/ErrorRate feed Kitchen prep time & food-event chance.
- **HeadChef**: see Section 2.
- **Busser**: Speed affects table turnover time (Seating bottleneck) and bathroom cleaning duration (§1.55). **Revised — coherence-check pass:** tables outrank a merely cleaning-eligible bathroom, but a bathroom that's actually past the Mood-costing threshold now outranks tables instead — see mechanics doc §13 for the exact tiered order. An already in-progress task is finished before switching, unless the player overrides.

**Fatigue → cost tradeoff**: fatigued staff = more errors/breakage/slow service = day-end costs + reputation risk. Core economic tension: shift length/breaks/staffing count vs wage cost.

**Full automatic mode**: every manual decision point (host seating, kitchen course-sync, waiter action choice, staff task priority) has an efficient heuristic fallback via `AutoManager`, each decision point backed by its own swappable strategy interface (`ISeatingStrategy`, `IKitchenSyncStrategy`, `IWaiterActionStrategy`, `IStaffTaskPriorityStrategy`) — see architecture doc §5/§11. MES (kitchen scheduling) is always automated regardless of AutoManager's on/off state; other systems are toggleable per-session via `Restaurant.AutoManagerToggles`, defaulting all-on.

---

## 4. Economy

- **Revenue**: sum of paid bills (item prices + upsell effects).
- **Tips**: go to **staff morale**, not restaurant income directly (see below).
- **Recurring day-end costs**: staff wages (hours worked, possibly overtime/fatigue-scaled), cleaning fee, ingredient/inventory restock, equipment maintenance, utilities.
- **Event-driven costs**: breakage (glass/plate, random trigger tied to low customer Mood/staff fatigue), food waste (undercooked/wrong items discarded), reputation-driven refunds/comped meals.
- **Reputation as economic multiplier**: higher reputation → higher arrival rate + bigger groups + slightly higher starting Mood/effective patience.
- **Pricing**: player sets menu prices; each `MenuItem` ships with a `DefaultPrice`. Too high suppresses conversion via `SpendingTendency`; too low hurts margin.
- **Tips → Morale mechanic**: tips accumulate per staff member/role pool → feed `Morale`. Low morale over time → higher `ErrorRate`, lower `Speed`, eventual quit risk (rehiring/training cost). Understaffing/bad management → worse service → worse tips → worse morale → death spiral risk the player must manage.
- **Day-end summary**: revenue, itemized costs, net profit, reputation delta. **Triggered by `EndDayCommand`, added — coherence-check pass** (mechanics doc §15) — fires once the restaurant has closed for new arrivals and fully cleared out (no queue, no seated groups), not on a fixed tick count.

---

## 5. Random Events

Base `RestaurantEvent`: trigger condition, duration, effect(s), resolution. Framework is extensible (future: health scare, celebrity visit, supply shortage, power outage, etc.) — a new event type's *resolution condition* (when it ends) is defined via its own `IEventResolutionStrategy` implementation (architecture doc §5.5), and its *effects* via its own `ISimulationModule` (entity doc §21's extension pattern) — together these mean a new event type never requires editing core logic, regardless of how unrelated its resolution/effects are to existing types.

- **Inspection**: purely random trigger (not reputation-scaled — any restaurant, good or bad practices, can be inspected). Max 1 active at a time; hard cooldown (N days) after resolution before it can trigger again. Checks cleanliness/compliance-type stats → pass/fail/grade → reputation swing + possible fine.
  - **Complaints book** ("Livro de reclamações" in Portugal — a legally mandated complaints book real Portuguese establishments must provide): an unsatisfied customer (low Mood, more likely if also high `Instability`) has a chance to file a formal complaint, which raises Inspection trigger probability for a period afterward. Doesn't guarantee an inspection, but skews odds. If Inspection then finds real issues → fine + reputation hit; if practices are fine → minor/no consequence.
- **Food Critic**: behaves like a VIP customer (heavier scrutiny across wait time, food quality, service) → larger reputation swing than a normal customer, positive or negative. Multiple can be concurrent, but stacking is less likely than Celebrity (see below). Starting `Mood` and `PatienceLevel` skew slightly lower/stricter than a typical customer's — a critic is a tougher grade by default (DLC-tunable).
- **Celebrity**: reputation-scaled spawn chance (higher reputation → higher chance); multiple can be concurrent, more freely than Food Critic.
- **Reputation-scaled trigger + self-suppression** (Celebrity & Food Critic): once one of a given type is active, chance of another *same-type* event triggering drops sharply; this suppression **weakens as reputation grows** (high-reputation restaurant absorbs more simultaneous critics/celebrities before suppression kicks in hard). Self-suppression is strictly same-type — no cross-type exclusivity is defined, so e.g. an Inspection and a Celebrity can be active concurrently.
- **Celebrity presence effect (DLC)**: while a celebrity is present, customers who arrive afterward can get a small `Mood`/`PatienceLevel` boost (the buzz of being somewhere a celebrity is). A regular/typical customer profile could similarly carry its own small innate Mood/Patience boost vs. a first-time visitor. These per-profile modifiers are data on the customer-type definition, not special-cased logic.
- **Open, not yet decided:** the roll cadence for each `EventType` — how often the game checks "should an Inspection/Critic/Celebrity trigger now," and the base probability behind it — is not locked anywhere in this doc or the mechanics doc. Flagged during the coherence-check pass; needs a decision before Phase 8 (architecture doc §10) implementation, not guessed here.

---

## 6. Post-MVP Systems ("DLC")

- **Inventory/Supply chain**: `Ingredient` stock levels per item, scheduled restock orders, supplier `Reliability` (late/partial delivery chance) and fluctuating `Price`, spoilage over time, item goes "86'd" (unavailable) if stock runs out mid-service.
- **Menu design & recipe cost**: each `MenuItem` has a recipe (ingredients + quantities), `Cost = sum(ingredient costs)`, `Margin = Price - Cost`. Possible limited-time/seasonal items, combo/set menus as bundled `MenuItem` groups.
- **Ambiance/decor**: `DecorItem` placeables (grid + hitbox, like kitchen stations) contribute to `AmbianceScore` — raises Mood recovery rate while seated and raises the reputation ceiling (great food alone can't hit max reputation without decor unlocking the upper band).
- **Upgrades/progression**: unlockable equipment tiers (station speed/capacity), kitchen expansion (more grid space), new menu categories, more tables/sections — gated by money and/or reputation milestones.
- **Staff hiring/training**: hiring pool refreshes periodically with randomized stat-rolled candidates, wage scales with stat quality. Training = spend money/time to raise a stat incrementally. Firing has a morale cost to remaining staff.
- **Reservation edge cases**: no-show (table held past grace window, then released, small trust/reputation penalty), late arrival within grace = seated normally, overbooking as a configurable risk toggle (auto-mode won't overbook; manual mode could for higher throughput at higher risk).
- **Marketing/advertising**: spend to temporarily boost reputation or arrival rate directly; diminishing returns if overused; ROI has variance, not guaranteed.
- **Seasonality/special occasions**: calendar system flags holidays/weekends (Valentine's, New Year's, etc.) → arrival rate + group size + spending tendency modifiers stack on base spawn formula. Reflected in restaurant decoration too — seasonal decor items, purchasable at a cost, time-limited relevance. **Deliberately not modeled as a `RestaurantEvent`** (entity doc §21) — a season/holiday window is calendar-driven (spans work-days, not a triggered/resolved occurrence like Inspection/Critic/Celebrity) and belongs alongside `DecorItem`/`AmbianceScore` as its own DLC-module concern, not another `EventType` string value. **Starting proposal, not locked:** a string-keyed, variable-length `SeasonDefinition` cycle (mechanics doc §15) — same extension shape as `MealPeriodDefinition` (entity doc §24a), driven by `Restaurant.CurrentDayNumber` (entity doc §19). 7 days is Duarte's proposed default per season, not a fixed rule — season length varies per `SeasonDefinition` and new seasons are pure data. Only this indexing shape exists now; the seasonal effects themselves (arrival/Mood modifiers, decor synergy) remain fully undesigned.
- **Takeout/delivery**: parallel `OrderChannel` — competes for kitchen station time and ingredient stock like dine-in, but skips table/seating/waiter entirely. Revenue without room-management overhead, but lower margin (delivery fees/packaging cost).
- **Statistics window**: dedicated stats system with historical time-series data — revenue, expense breakdown, reputation over time, customer satisfaction trends, staff performance, event history. Implies a `StatisticsSnapshot`-per-day (or per-tick) logging layer built into the architecture from the start, not bolted on later.
- **Non-rectangular placeables**: irregular-footprint furniture/equipment (L-shaped bar counter, corner booth, etc.), beyond the plain rectangles every MVP entity uses. `IPlaceable`'s `OccupiedCells` field (entity doc §1) and the pathfinder's footprint resolution (mechanics doc §9) already support this — adding a real non-rectangular object later is additive (new entity + populated field), not a rework.

---

## Core Architectural Principles (apply to every system above)

1. **Data-driven, preset-agnostic** — no hardcoded restaurant configurations; `RestaurantPreset` defines starting money, layout, staff, menu, reputation, decor, etc. as pure data.
2. **Reusable simulation engine** — build for extension by others, not just this one game.
3. **Auto-manager AI layer** — every manual decision point must have an efficient automated equivalent (design validation tool + player accessibility feature).
4. **Config over hardcoding** — layouts (kitchen, decor), pricing, event pools, presets are all data, not code.
5. **Endless simulation** — no win/loss condition in the core loop.

---

## Next Steps (as of this doc)

1. ~~Architecture (C# backend, sim loop/tick model, project structure)~~ — **done**, see `restaurant-simulator-architecture.md` (Blazor WASM + SignalR, not the Angular placeholder originally noted here)
2. ~~Data model draft~~ — **done**, see `restaurant-simulator-entities.md`
3. ~~Agent files (project conventions/instructions)~~ — **done**, see `CLAUDE.md`
