# Restaurant Simulator — Entity Reference

Companion to `restaurant-simulator-business-logic.md`. This file documents the
finalized data model: every entity, its fields, and the reasoning behind each
design decision. Built incrementally, one entity at a time. Nothing here
overrides the business logic doc — it's the structural expression of it.

Status legend: 🔒 Locked · 🚧 In progress · ⏳ Not yet discussed

## Table of Contents

1. 🔒 IPlaceable (shared contract)
2. 🔒 TableShape
3. 🔒 Table
4. 🔒 Seat
5. 🔒 TableCluster
6. 🔒 StaffMember
7. 🔒 StaffStats
8. 🔒 CustomerGroup
9. 🔒 Customer
10. 🔒 CustomerCall
11. 🔒 ComplaintRecord
12. 🔒 Order
13. 🔒 OrderItem
14. 🔒 KitchenTicket
15. 🔒 HeatLamp
16. 🔒 KitchenStationType
17. 🔒 KitchenStation
18. 🔒 MenuItem
18a. 🔒 Freezer (added in coherence-check pass)
19. 🔒 Restaurant (aggregate root)
20. 🔒 Reservation
21. 🔒 RestaurantEvent
22. 🔒 RestaurantConfig
23. 🔒 StatisticsSnapshot
24. 🔒 Bathroom (added in concurrency-sweep session)
24a. 🔒 MealPeriodDefinition (added in coherence-check pass)

---

## 1. IPlaceable (shared contract, not an entity)

**Why it exists:** Tables, kitchen stations (§2 of business logic), and future
decor items (§6) all need grid position + collision footprint. Rather than
duplicating this on each entity, it's one shared contract all three implement.
This is the kind of reuse the project's "generalizable engine" goal calls for.

| Field | Type | Notes |
|---|---|---|
| `Position` | `GridPosition (int X, int Y)` | Integer grid cell, not continuous coordinates — MVP has no free placement; grid-cell math also drives adjacency checks for merging and future DLC collision checks |
| `Hitbox` | `GridBounds (Width, Height in cells)` | Bounding-box footprint on the grid. **All current MVP entities** (`Table`, `KitchenStation`, `HeatLamp`, `Freezer`, `Bathroom`) are plain rectangles and are fully described by this alone |
| `OccupiedCells` | `IReadOnlyList<GridPosition>?` | **Added — DLC-extensibility pass.** Null for every current entity (default; means "fully solid rectangle per `Hitbox`"). When a future DLC object needs a non-rectangular footprint (L-shaped counter, corner booth, etc.), it supplies the exact set of occupied cells relative to `Position` here instead — the bounding `Hitbox` on that object then just describes the smallest rectangle containing those cells, for quick coarse checks, while `OccupiedCells` is authoritative for actual collision |

**Collision/pathfinding consumers must resolve footprint as:**
`OccupiedCells ?? all cells within Hitbox` — a single fallback rule, checked
uniformly regardless of shape (see mechanics doc §9). No entity currently
built needs the non-null path; this is forward scaffolding so a future
DLC module introducing an irregular-shaped placeable is an **additive**
change (new entity + populated `OccupiedCells`) rather than another
breaking change to `IPlaceable` and every one of its five current
consumers. Same treatment already given to `MenuItem.PickupSourceType`
sitting unused until `Freezer` needed it.

---

## 2. TableShape (preset/data, referenced by Table)

**Why it exists:** Avoids an inheritance hierarchy (`RoundTable : Table`, etc.)
for what is really just data variation. A table's shape — seat count, seat
positions/orientations, whether it supports merging — is fully defined by a
preset, loaded once and shared by reference. New shapes (booth, bar counter,
round 8-top) are added as new preset entries, zero new code.

| Field | Type | Notes |
|---|---|---|
| `ShapeId` | `string` | Preset key (e.g. `"standard_2top"`, `"round_8top"`) |
| `SeatDefinitions` | `IReadOnlyList<SeatDefinition>` | Local seat index, relative offset, orientation — see Seat below |
| `CanMerge` | `bool` | Hard gate on merge eligibility. Checked **first**, before any geometry/orientation logic — e.g. round tables ship with `CanMerge = false`, so they're rejected immediately regardless of how their seats are arranged. Geometry is never even evaluated for a shape that can't merge. |

---

## 3. Table (physical furniture)

**Fulfills:** Fixed position/seat count for MVP (per your instruction), reusable
placement contract, dynamic-section support (see TableCluster below), and the
doc's §2 grid/hitbox placement pattern applied consistently across the game.

| Field | Type | Notes |
|---|---|---|
| `TableId` | `int` | Small fixed set, array-indexable, cheaper than Guid for hot-path lookups |
| `Position`, `Hitbox` | via `IPlaceable` | Fixed for MVP (no player rearranging); DLC move-mode will only ever touch these two fields, nothing downstream |
| `Shape` | `TableShape` (ref) | Drives seat count/layout/merge eligibility — always `standard_2top` in MVP |
| `Seats` | `IReadOnlyList<Seat>` | Generated from `Shape.SeatDefinitions`, always 2 in MVP |
| `ClusterId` | `int?` | Which `TableCluster` this table currently belongs to; null if standalone. Bidirectional reference (cluster also lists its `TableIds`) — chosen over one-directional because seating/bussing logic needs fast "what cluster is this table part of" lookups constantly; sync risk is contained since only merge/split commands ever write it |
| `IsClean` | `bool` | **Added in concurrency-sweep session.** Cleanliness lives on the individual table, not the cluster — a merged cluster's tables can be dirty independently, and a clean table can be split off and used/merged separately from a still-dirty one. `TableCluster.State` is derived from this (see TableCluster below), not the other way around. Set to `false` when a group leaves an occupied table; Busser cleaning progresses this per-tick (see mechanics doc §13) and flips it to `true` on completion |

Note: **no `Section` field.** Section is not a property of furniture — see
TableCluster, where it's derived dynamically from waiter assignment.

---

## 4. Seat

**Fulfills:** Per-customer food delivery accuracy (the "does the right person
get their order" fix), realistic chair-facing per your description, and the
merge-axis rule that keeps merged tables geometrically valid without needing
to hide/remove chairs.

| Field | Type | Notes |
|---|---|---|
| `SeatIndex` | `int` | Local index within the table (0 or 1 in MVP) |
| `RelativeOffset` | `Vector2` | Position relative to the table's `Position`, for frontend chair placement |
| `Orientation` | `enum: North, South, East, West` | Which way the seat faces. For a standard 2-top, both seats share either the same X (facing each other N/S) or same Y (facing each other E/W) — matches real restaurant layouts. **Known gap, deferred:** cardinal-only orientation can't precisely describe a seat on a round/hex table's diagonal position — not a blocker, since `CanMerge = false` on such shapes (§2) means the only logic that actually depends on strict N/S/E/W (the merge-axis rule below) never runs for them; this only limits rendering precision for a future non-rectangular `TableShape`, not gameplay logic |
| `OccupiedBy` | `CustomerId?` | Enables exact per-customer order routing |

**Merge-axis rule:** two tables can only merge along the axis *perpendicular*
to their seat orientation (push together left-right if seats face N/S, or
top-bottom if seats face E/W). This guarantees no seat ever overlaps another
after a merge, so nothing needs to be visually removed/hidden — a case that
was considered and deliberately deferred (see rationale below).

---

## 5. TableCluster (the actual seating/occupancy unit)

**This is what the business-logic doc's §1.3 calls `Table`.** Once physical
furniture (`Table`) was split from the occupiable seating unit, the doc's
`Table.Status` (Free/Reserved/Occupied/NeedsCleaning) properly belongs here.

**Fulfills:** doc §1.3 status vocabulary exactly, dynamic section/waiter
balancing per your description, auto-dissolve-on-clean flow already agreed,
same-section-only merge safety (see rule below).

| Field | Type | Notes |
|---|---|---|
| `ClusterId` | `int` | Identity |
| `TableIds` | `IReadOnlyList<int>` | 1 (standalone) to N (merged) physical tables |
| `Capacity` | `int` | Sum of member seats — always even in MVP (2, 4, 6) |
| `State` | `enum: Free, Reserved, Occupied, NeedsCleaning` (derived, not stored) | **Changed in concurrency-sweep session — was stored, now computed.** Priority order: `NeedsCleaning` if any member `Table.IsClean == false`; else `Occupied` if `OccupiedBy != null`; else `Reserved` if an active `Reservation` currently holds this cluster (see Reservation §20 for the exact query — no stored backlink, derived same as `IsMerged`); else `Free`. Same derivation pattern as `IsMerged` below — avoids a second source of truth now that cleanliness lives per-`Table` |
| `OccupiedBy` | `GroupId?` | Currently seated group, null otherwise |
| `AssignedWaiterId` | `StaffId?` | Replaces the static `Section` idea — a cluster's serving waiter, not a fixed geographic zone |
| `ManuallyAssigned` | `bool` | If true, the auto section-balancing algorithm won't overwrite `AssignedWaiterId` on its next recompute — preserves player overrides |
| `AnchorPosition` | `GridPosition` (derived, e.g. lowest `TableId`'s position) | For staff pathfinding only — not authoritative state, just a convenience reference point. No table is "promoted" to primary; the cluster is its own identity |
| `IsMerged` | `bool` (derived: `TableIds.Count > 1`) | Not stored, to avoid a second source of truth |
| `RecentJokeCount` | `int` | **Added in concurrency-sweep session.** Tracks jokes told at this table this visit (player or AutoManager) — feeds `JokeFatigueMultiplier` (mechanics doc §11). Resets to 0 when `State` transitions `NeedsCleaning → Free` (fatigue is within-visit, not persistent across parties) |
| `LastJokeTick` | `long` | **Added in concurrency-sweep session.** Set on every joke told at this table. Feeds only the AutoManager autonomous-joke cooldown check — player-triggered jokes aren't blocked by it but still update it |

**Same-axis merge rule:** merging two tables is only valid along the axis
perpendicular to shared seat orientation (see Seat above) — this both keeps
seats non-overlapping and, incidentally, keeps a merged cluster's shape
sensible for rendering.

**Dynamic section/waiter balancing (not stored on the entity — a service):**
A `SectionAssignmentService` recomputes `AssignedWaiterId` across all clusters
whenever active waiter count changes — partitions clusters into N contiguous
groups via adjacency (flood-fill over `Table.Position`), balancing table count
per waiter (e.g. 10 tables / 4 waiters → 2/2/3/3). Player can manually
reassign a cluster to a different waiter, validated against the same
contiguity check; flips `ManuallyAssigned = true` so auto-recompute leaves it
alone going forward.

**Changed in concurrency-sweep session:** this recompute must be triggered
via a real dispatched command (`RecomputeSectionAssignmentsCommand`), not an
inline side-effect fired directly off a staff-count change. An inline
side-effect would bypass the per-restaurant lock (see architecture doc's new
Concurrency section) that every other state mutation goes through — routing
it through the normal command pipeline keeps it inside that same guarantee.

**Considered and deferred:** visually hiding/removing chairs during a merge
to make room. Not needed given the merge-axis rule (no overlap ever occurs),
so left as a pure rendering nuance to revisit only if a future DLC merge case
actually produces overlap.

**No merge cap.** No maximum on `TableIds.Count` — any number of tables can be
merged in a line, limited only by `CanMerge`, same-axis, and adjacency rules
above. Block-shaped merges (2×2, banquet layouts) are **not** supported by the
single-axis rule — flagged as a known gap, deferred to DLC if wanted.

---

## Architecture note — StaffMember diverges from the business logic doc

Business logic §3 describes a class hierarchy: `FrontOfHouseStaff` (base) →
`Waiter`, `Host`; `Cook` → `HeadChef`. The Architecture chat settled on a
single concrete `StaffMember` class with a `Role` enum and behavior resolved
via `IStaffBehavior[Role]` lookup instead — avoids virtual-dispatch cost in
the tick loop, and adding a new role is registering a new behavior handler
rather than a new subclass. Every behavior the doc describes is preserved;
only the mechanism changed. Documented here so the two sources don't silently
drift apart.

---

## 6. StaffMember

**Fulfills:** doc §3's full role list, shared-command flexibility (a Waiter
can cover Host seating tasks when Host is busy), fatigue/error mechanics.

| Field | Type | Notes |
|---|---|---|
| `StaffId` | `int` | Small fixed MVP roster, array-indexable |
| `Name` | `string` | Display |
| `Role` | `StaffRole (enum: Waiter, Host, Cook, HeadChef, Busser)` | Drives behavior via `IStaffBehavior[Role]` dispatch, not inheritance |
| `State` | `enum: Idle, Busy, Resting, OffShift` | `Resting` reserved for the future stamina/break DLC; unused in MVP. `OffShift` — **added, coherence-check pass** — a staff member outside their full daily shift span (earliest-to-latest across all their `AssignedMealPeriods` for the day, not each period individually); see §24a for the exact `ShiftStart`/`ShiftEnd` definition and the Fatigue tie-in this resolves |
| `CurrentTaskType` | `TaskType (enum: SeatGroup, CleanTable, CleanBathroom, DeliverOrderItem, AcknowledgeCall, CookItem)?` | What this staff member is currently doing; `null` when `State == Idle`. See "No dedicated `Task` entity" below for why this is two inlined fields rather than a `TaskId` reference |
| `CurrentTaskTargetId` | `int?` | The target this task acts on, interpreted per `CurrentTaskType` — a `TableId` for `CleanTable`, `BathroomId` for `CleanBathroom`, `OrderItemId` for `DeliverOrderItem`/`CookItem`, `CallId` for `AcknowledgeCall`, `GroupId` for `SeatGroup`. Always set together with `CurrentTaskType` (both null or both populated) |
| `Stats` | `StaffStats` | See below |
| `AssignedMealPeriods` | `IReadOnlyList<string>` | **Added — coherence-check pass.** Which periods (referencing `MealPeriodDefinition.MealPeriodId`, §24a) this staff member is rostered for; validated as a subset of `Restaurant`'s own `ActiveMealPeriods` at assignment/hire time |

**No dedicated `Task` entity.** Considered — `TaskId`/`TaskType` were
referenced by name in earlier drafts of this doc without ever being
defined — and rejected in favor of the two inlined fields above.
Reasoning: unlike `CustomerCall` (entity doc §10), which genuinely needs
to exist independently — it lives in `Restaurant.PendingCalls`, is scanned
by a dispatcher across all waiters, and can transfer between staff — a
`Task` has no independent existence to protect. The actual "work to be
done" already lives as state on the target entity itself
(`Table.IsClean == false`, `Bathroom.CleanlinessScore`,
`OrderItem.Status == Ready`, `CustomerCall.Status == Pending`); a
dispatcher scans *those* fields directly and assigns a staff member — it
never needs to scan a queue of `Task` objects. Mechanics doc §13 already
confirms interruption/resume progress lives on the target
(`Table`/`Bathroom`), not on a task wrapper, so a `Task` entity would have
nothing left to own beyond "which staff member, which target" — exactly
what the two inlined fields already capture, with no second ID space to
manage or garbage-collect. Completing or cancelling a task is just
clearing both fields back to `null`; the "check all, then commit all"
override rule (mechanics doc §9/§13) applies the same way it would to a
real entity — validate the new target first, only then overwrite.

If a future system (e.g. MES v2's multi-cook-per-item scheduling,
mechanics doc §10) genuinely needs a task to be a richer shared object,
that's an additive extension at that point — not something to
pre-design speculatively here.

**No `AssignedStationId` field.** Considered, then dropped: you clarified a
Cook can work any station, one at a time, with no permanent specialization
for now. Station assignment lives entirely on the current task (resolved
via `CurrentTaskType`/`CurrentTaskTargetId` — a `CookItem` task's target
`OrderItem.StationId` says which station), not the staff member — a Cook
is simply idle/busy, and `Busy` means occupied by whatever task (and
station) they were last dispatched to. Avoids modeling specialization that
doesn't exist yet; adding a real `Specialization` stat later is additive.

**No `AssignedClusters` list.** A waiter's tables are found by querying
`TableCluster`s where `AssignedWaiterId == StaffId` — kept one-directional
(cluster → staff, not staff → clusters) to avoid a second list that could
desync from the cluster-side truth. Cheap query, small cluster count.

**Shared-command coverage (confirms doc's implicit staff flexibility):**
`TaskCapabilities[TaskType.SeatGroup] = [Host, Waiter]` — dispatcher tries
idle Host first, falls back to idle Waiter if Host is busy. This is a data
table (`Dictionary<TaskType, StaffRole[]>`) living in a dispatch
service/preset config, not on any entity — any role can be made capable of
any task type by editing config, no code change.

**HeadChef singleton rule** — enforced at the command layer (any assignment
of `Role.HeadChef` checks no other active HeadChef exists first), not stored
as entity state. Ties into doc §2: no HeadChef present → kitchen-wide quality
malus, applied by the Kitchen module reading current roster each tick.

---

## 7. StaffStats

Open stat-bag (`Dictionary<StatType, float>`) rather than fixed fields per
stat — additive when new stats are needed later (e.g. a DLC training system,
or a Cook-only precision stat), no schema change required.

| StatType | Used by (per doc §3) |
|---|---|
| `Speed` | All roles — waiter service speed, cook prep speed, busser turnover speed |
| `Fatigue` | All roles — rises over shift, decays on break/`Resting`, degrades `Speed`, worsens `ErrorRate` |
| `ErrorRate` | Cook (wrong/undercooked items) — base value, worsened by current `Fatigue` |
| `Charisma` | Waiter (soothing/joke/upsell/check-in rolls vs customer Mood), Host (handling agitated queued groups) |
| `Experience` | All roles — seniority/level, no formula defined yet, left open per doc |
| `Morale` | All roles — fed by accumulated tips (business logic §4), not customer-facing. Low `Morale` worsens `ErrorRate`/`Speed` and raises quit risk; exact formula open, pending playtesting (**added in coherence-check pass** — referenced by business logic §4 but previously undefined anywhere) |

---

## Note — dispatch has two distinct rules, don't conflate them

1. **Seating new arrivals** (no assigned waiter yet, table free): `TaskCapabilities[TaskType.SeatGroup] = [Host, Waiter]` — any idle capable staff, Host preferred, Waiter fallback.
2. **CustomerCall at an occupied, already-assigned table**: routes to that `TableCluster.AssignedWaiterId` first. If they're unavailable, any idle Waiter can pick up the call as temporary coverage — section ownership (`AssignedWaiterId`) itself doesn't change, just who answers this call. If no waiter is idle at all, the call keeps accumulating attempts/Mood penalty — true understaffing should still visibly cost the section, reinforcing the efficiency-management theme.

---

## 8. CustomerGroup

**Fulfills:** doc §1.1 arrival/group profile, the course-phase/auto-visit/call
flow settled earlier, bill-payer designation, kids-menu enforcement (via
`Customer.IsAdult`, see below).

| Field | Type | Notes |
|---|---|---|
| `GroupId` | `int` | Identity |
| `PartySize` | `int` | 1–6, skewed toward 2, per doc §1.1 |
| `Members` | `IReadOnlyList<Customer>` | See below — carries the per-person traits |
| `ClusterId` | `int?` | Assigned `TableCluster`; null while queued |
| `ReservationId` | `int?` | Set if this group originated from a reservation |
| `LifecycleState` | `enum: Arriving, Queued, Seated, Leaving` | Coarse state; once `Seated`, `CoursePhase` tracks finer progress |
| `ArrivalTick` | `long` | For queue-wait and stats |
| `CoursePhase` | `enum: Seated, Drinks, EntryMain, Dessert, Bill, Done` | Table-wide — whole table always shares one phase, confirmed earlier |
| `PhaseWaitState` | `enum: AutoVisitPending, Serving, AwaitingDecision, RecheckScheduled` | Drives auto-visit → not-ready → recheck/call-preempt timing |
| `NextAutoVisitTick` | `long` | Recheck timer target, if no call preempts it first |
| `CurrentOrderId` | `int?` | Active `Order` for this group |
| `PayerId` | `CustomerId?` | Designated bill payer — MVP: first adult member. Bill-splitting (DLC) reads existing `OrderItem.OwnerId` at payment time; no schema change needed for that extension |

**No group-level `PatienceLevel`/`SpendingTendency`/`IsRegular`.** Moved to
per-person (see `Customer`) — these directly interact with per-person `Mood`,
`Instability`, and order generation (via `OrderItem.OwnerId`), so keeping
them at group level was inconsistent with everything else already being
per-person. Where a group-wide read is genuinely needed (e.g. seating
priority for regulars), it's **derived on demand** —
e.g. `HasRegular = Members.Any(m => m.IsRegular)` — never stored separately,
to avoid a second source of truth.

**Group Mood is derived**, not stored — computed as an average/spread over
`Members[i].Mood` wherever needed (walkout checks, complaint escalation,
inter-group spread rolls).

---

## 9. Customer

**Fulfills:** per-customer Mood/Instability/Susceptibility (doc §1.1),
accurate food routing (`SeatIndex`), adult/kid role separation (calling,
paying, kids-menu enforcement — your addition), and now per-person
patience/spending/regular status.

| Field | Type | Notes |
|---|---|---|
| `CustomerId` | `int` | Identity |
| `SeatIndex` | `int?` | Physical seat, null until seated — pairs with `Seat.OccupiedBy` for order routing |
| `IsAdult` | `bool` | Gates calling (`RequestNextCourse`/`RequestItem`/`RequestBill`), paying, and Kids-menu-only ordering. Also used for "calmest adult" complaint-voicing (doc) and stronger calming pull in intra-group spread |
| `Mood` | `float (0–100)` | Dynamic. Read on every complaint check and at payment (tip calc) |
| `PatienceLevel` | `float` | **Moved from group to per-person.** Modifier on how fast this individual's Mood rises/falls — a kid and a parent in the same group can differ |
| `Instability` | `float` | Fixed for the visit. Severity/threshold multiplier on complaint rolls at a given Mood level |
| `SusceptibilityToGroup` | `float` | Per-person pull toward group-average Mood (intra-group spread); adults/parents have stronger calming influence per doc's own example |
| `SpendingTendency` | `enum: Budget, Normal, Generous` | **Moved from group to per-person.** Needed at this granularity since `OrderItem.OwnerId` already tracks individual orders — group-level spending had no per-person signal to actually drive from |
| `IsRegular` | `bool` | **Moved from group to per-person.** One member (e.g. a parent) can be a regular while others in the group aren't |
| `IsPresentAtTable` | `bool` | **Added in concurrency-sweep session.** True while seated at the table; false while away (bathroom, etc.). Gates joke resolution (mechanics doc §11), the group-presence requirement for group-wide calls (business logic §1.55), and intra-group Mood spread (mechanics doc §4) — absent members are excluded from all three. `Mood` is also frozen (no decay/recovery/spread) while `false` |
| `Gender` | `enum: Male, Female` | **Added in concurrency-sweep session.** Routes which gendered `Bathroom` queue this customer joins (§12 of mechanics doc) |
| `BathroomVisitCount` | `int` | **Revised — coherence-check pass, replaces `HasUsedBathroom: bool`.** Increments on each bathroom trip this visit; feeds the logarithmic decay in the go-to-bathroom roll (mechanics doc §12) instead of a hard once-per-visit cap |
| `LastBathroomVisitEndTick` | `long?` | **Added — coherence-check pass.** Set when a bathroom visit concludes (on return to table); gates the per-visit cooldown before another trip is eligible (mechanics doc §12) |

**Call frequency, not a stored field — behavior note:** the likelihood of
this customer initiating/repeating a `CustomerCall` scales inversely with
current `Mood` **only**. `PatienceLevel` already did its work upstream — it's
a fixed trait governing how fast Mood itself moved to get here; using it
again as a second input into the call-frequency roll would double-count the
same trait. Mood is the single downstream signal to read. Computed each tick
from existing fields, nothing new to store.

---

## 10. CustomerCall

**Fulfills:** the full call lifecycle from the Ordering discussion — creation,
acknowledgment, staff fallibility/stalling, retry cap, and now single-waiter
routing.

| Field | Type | Notes |
|---|---|---|
| `CallId` | `int` | Identity |
| `ClusterId` | `int` | Table-level, not customer-level — any adult member can trigger; the assigned waiter resolves **all** pending calls for that cluster in one visit |
| `Type` | `enum: RequestNextCourse, RequestItem, RequestBill` | `RequestNextCourse` is the timer-preempting one; `RequestItem`/`RequestBill` can fire at any time, independent of phase timing |
| `Status` | `enum: Pending, Acknowledged, Resolved` | `Acknowledged` = waiter noticed and queued the task ("I'll be right there," visible to the customer) but hasn't completed it yet |
| `AttemptCount` | `int` (max `MaxCallAttempts`) | Re-calling while still `Pending` increments this on the same record rather than creating duplicates. Rate of re-calling is driven by the caller's current `Mood`/`PatienceLevel`. **Corrected, coherence-check pass:** cap was previously a bare hardcoded `3` with no backing field; now `MaxCallAttempts` (`RestaurantConfig`, entity doc §22), default `3` |
| `CalledAtTick` | `long` | Initial call time — for wait stats and notice-chance rolls |
| `AcknowledgedAtTick` | `long?` | Set when a waiter acknowledges. A **stall check** applies afterward: if too long passes without reaching `Resolved`, the call re-escalates (Mood keeps dropping, can still count toward the attempt cap) — models a waiter forgetting/erring, not just not-hearing |

**Added in concurrency-sweep session:** picking up/acknowledging a call must
be a real named command, `AcknowledgeCallCommand` — not implicit. Under the
per-restaurant lock, two waiters racing to pick up the same unassigned call
is already safe by construction (whichever command processes first wins;
the second re-validates `Status == Pending` at handle-time and is rejected
via `Result<T>` if it's already `Acknowledged`) — this note just formalizes
the command's existence and name, which wasn't previously listed anywhere.

**Routing constraint:** `TableCluster.AssignedWaiterId` is the primary and
first-priority responder for calls at that cluster. If that staff member is
unavailable (`Resting`, or otherwise not `Idle`), the call becomes eligible
for pickup by **any idle Waiter** — a temporary coverage assignment, not a
change to `AssignedWaiterId`/section ownership. Once the assigned waiter
returns to `Idle`, they resume owning the section normally. This is
dispatcher logic (reads `StaffMember.State` across all waiters), not a stored
field on `CustomerCall`. If no waiter is idle at all, the call keeps
accumulating attempts/Mood penalty — genuine understaffing should still cost
the restaurant, just not a single unlucky break timing.

Lives in `Restaurant.PendingCalls` (flat list), not nested in `CustomerGroup`
— calls fire independent of course-phase state, and a waiter's own tick logic
needs to scan for their assigned calls efficiently across the whole floor.

---

## 11. ComplaintRecord

**Fulfills:** doc §5's complaints-book mechanic (*"an unsatisfied customer...
has a chance to file a formal complaint, which raises Inspection trigger
probability for a period afterward"*) — with weighting, since a late-food
complaint and a wrong-order-plus-rudeness complaint shouldn't count equally.

| Field | Type | Notes |
|---|---|---|
| `ComplaintId` | `int` | Identity |
| `GroupId` | `int` | Who filed it |
| `Category` | `ComplaintCategory (enum: LongWait, WrongOrder, FoodQuality, Rudeness, Cleanliness, Other)` | What it's actually about — the basis for severity weighting |
| `Severity` | `float` | Derived at creation: `Category` base weight (data, tunable per preset) × filer's `Mood`/`Instability` at time of filing. A wrong/cold-food complaint from a high-Instability customer weighs more than a long-wait complaint from a low-Instability one |
| `FiledAtTick` | `long` | For decay/cooldown tracking |

**Inspection probability boost (doc §5), revised with severity in mind:**
rather than a flat per-complaint bump, the boost is `Σ(active complaint
severities)`, with each complaint's contribution decaying over time after
filing. A spree of minor complaints and one severe complaint can now produce
meaningfully different Inspection odds, rather than counting identically.

`Category` base weights live in preset/config data (same data-driven pattern
as everything else), not hardcoded — tunable per restaurant type without
touching code.

---

## 12. Order

One running order per group, accumulating items across all course phases —
not one Order per phase — since doc §1.4 treats ordering as a continuous
fixed sequence within a single visit.

| Field | Type | Notes |
|---|---|---|
| `OrderId` | `int` | Identity |
| `GroupId` | `int` | Owning group |
| `ClusterId` | `int` | Redundant with the group's own `ClusterId`, kept here to avoid a join on the hot kitchen-scan path |
| `Items` | `IReadOnlyList<OrderItem>` | Accumulates across all phases |
| `CreatedAtTick` | `long` | First item added (Drinks phase) |

**No `TotalAmount` field** — derived on demand at bill time
(`Items.Sum(i => i.PriceAtOrder)`), since storing a running total risks going
stale if anything touches items after the fact.

---

## 13. OrderItem

One record per unit of food/drink — **no `Quantity` field.** A customer
wanting two drinks is two separate `OrderItem` records. This isn't just
realism (people don't order "2 plates") — it's mechanically required, since
`Status` is per-record and a `Quantity > 1` item couldn't represent one unit
being `Ready` while another is still `Cooking`.

| Field | Type | Notes |
|---|---|---|
| `OrderItemId` | `int` | Identity |
| `OwnerId` | `CustomerId` | Enables exact per-person food routing via `Seat.OccupiedBy` |
| `MenuItemId` | `int` | Ref to preset `MenuItem` |
| `Phase` | `enum: Drinks, EntryMain, Dessert` | Which course batch this belongs to — ties the item to a `KitchenTicket` |
| `PriceAtOrder` | `decimal` | Snapshot of `MenuItem.Price` at order time — protects revenue/stats integrity if the player changes prices mid-service |
| `Status` | `enum: Queued, Cooking, HeldUnderLamp, Ready, Served` | Per-item kitchen progress |
| `TicketId` | `int?` | Which `KitchenTicket` batch this item is scheduled under |
| `StationId` | `int?` | Which `KitchenStation` it's cooking at, once `Status == Cooking` |
| `HeldSinceTick` | `long?` | Set when `Status` becomes `HeldUnderLamp` — basis for computing `QualityAtServe` |
| `IsHighQuality` | `bool` | Rolled once at the `Cooking → Ready` transition (mechanics doc §10) — not stored until that point |

**Kids-menu enforcement** is command validation, not a field: `PlaceOrderCommand`
rejects any `OrderItem` where `OwnerId` resolves to a `Customer` with
`IsAdult == false` and `MenuItem.Category != Kids`.

**`QualityAtServe`** — computed at serve time from `HeldSinceTick` ×
`HeatLamp.QualityDecayRatePerTick` (if held at all), not stored per-tick.
Fed *by* the food-quality-event roll from doc §1.6 (via `IsHighQuality`/
`FoodQualityEvent`, see below and mechanics doc §10) — not the reverse.

**`IsHighQuality`** — rolled once at the `Cooking → Ready` transition
(mechanics doc §10). Mutually exclusive with the `FoodQualityEvent` roll,
which fires immediately after at that same transition for any item that
didn't roll high-quality. Both feed into `QualityAtServe` above alongside
hold time — see mechanics doc §10 for the full formula.

---

## 14. KitchenTicket

The sync-batch unit doc §2 describes: items submitted together (same phase,
same table) that the kitchen schedules to finish together.

| Field | Type | Notes |
|---|---|---|
| `TicketId` | `int` | Identity |
| `ClusterId` | `int` | For "how long has this table waited" lookups feeding the heat-lamp-vs-reinform decision |
| `ItemIds` | `IReadOnlyList<int>` | The `OrderItem`s in this batch |
| `SubmittedAtTick` | `long` | When the phase was taken |
| `TargetReadyTick` | `long` | Computed sync target — the scheduling algorithm's output (per-item start-time offsets, per doc §2's example) |

**No stored `State` field** — ticket-level status is derived from its items'
statuses (e.g. "Ready" only once every `ItemId` is `Ready` or `HeldUnderLamp`,
"InProgress" if any is `Cooking`), avoiding a second source of truth that
could desync from the actual item states — same principle as
`TableCluster.IsMerged`.

**Heat-lamp vs re-inform decision** (doc §2) — Kitchen-module logic reading
`TargetReadyTick` vs actual completion, the table's wait history, and now
`HeatLamp` slot availability (see below). Short delay + lamp has a free slot →
hold under lamp. Long delay, or lamp full regardless of delay length → waiter
informs table, ready items served immediately, table eats asynchronously. The
sync target is a goal, not a guarantee — a ticket can resolve with items
served at different times despite being one batch.

---

## 15. HeatLamp

A real, capacity-limited kitchen facility for quality-preserving hold —
distinct from a cooking station, since its role is preservation, not
preparation. Uses the shared `IPlaceable` contract, same as `Table`/
`KitchenStation`.

| Field | Type | Notes |
|---|---|---|
| `HeatLampId` | `int` | Identity |
| `Position`, `Hitbox` | via `IPlaceable` | MVP fixed layout, same as other kitchen equipment |
| `SlotCount` | `int` | Total hold capacity |
| `OccupiedSlots` | `IReadOnlyList<int>` (OrderItemIds) | What's currently being held; full when `Count >= SlotCount` |
| `QualityDecayRatePerTick` | `float` | How fast held food loses quality — basis for `OrderItem.QualityAtServe` |

**Real constraint on the heat-lamp decision:** if the lamp is full, "hold
under lamp" isn't available regardless of how short the delay is — forces
re-inform instead. Adds genuine resource pressure (build more lamp capacity?)
consistent with the game's efficiency-management theme, rather than treating
lamp-holding as a free, unlimited option.

---

## 16. KitchenStationType (preset/data, referenced by KitchenStation)

Same pattern as `TableShape` — station variation (grill, fryer, oven,
cold-prep, dessert station) is data, not a subclass per type. Adding a new
station type later is a new preset entry, zero new code.

| Field | Type | Notes |
|---|---|---|
| `StationTypeId` | `string` | Preset key, e.g. `"grill"`, `"fryer"`, `"oven"`, `"cold_prep"`, `"dessert_station"` |
| `Capacity` | `int` | Concurrent items this station type can cook at once — an oven plausibly holds more than a single fryer basket, purely data-defined |
| `SpeedMultiplier` | `float` | Base prep-time modifier for this type, layered under a Cook's own `Speed` stat |

`MenuItem` (next entity) will reference which `StationTypeId` it requires —
that's the link between a dish's needs and what capacity gates it.

---

## 17. KitchenStation (physical, placed instance)

| Field | Type | Notes |
|---|---|---|
| `StationId` | `int` | Identity |
| `Position`, `Hitbox` | via `IPlaceable` | Shared contract; MVP fixed layout per doc §2 |
| `StationType` | `KitchenStationType` (ref) | Drives capacity + speed modifier |
| `OccupiedSlots` | `IReadOnlyList<int>` (OrderItemIds) | Currently-cooking items here; full when `Count >= StationType.Capacity`, blocking new items — this is the "station contention" doc §2 flags as a sync-threatening delay |

**No `AssignedCookId`** — confirmed earlier, Cooks aren't bound to a specific
station, they're dispatched per-task. Multiple Cooks can work the same busy
station across a shift; occupancy is driven by what food is cooking there,
not by ownership.

**Ties to `OrderItem.Status`:** an item can only move `Queued → Cooking` once
a `KitchenStation` matching its `MenuItem`'s required `StationTypeId` has a
free slot — the actual resource-contention check feeding the heat-lamp
decision, not just prep-time arithmetic.

---

## 18. MenuItem

Preset/reference data, shared by pointer across all `OrderItem`s — not owned
per-instance. Kept deliberately clean of Inventory/recipe fields per the
earlier decision to defer that DLC system without guessing its shape now.

| Field | Type | Notes |
|---|---|---|
| `MenuItemId` | `int` | Identity |
| `Name` | `string` | Display |
| `Category` | `enum: Adult, Kids` | Doc §1.4 — kids items generally faster prep + lower price ceiling. Enforced at order time via `PlaceOrderCommand` |
| `Course` | `enum: Drinks, EntryMain, Dessert` | Which phase this item belongs to — values match `CustomerGroup.CoursePhase`/`OrderItem.Phase` (§8, §13) directly, so ordering-window validation is a straight equality check, not a translation table |
| `RequiredStationType` | `string?` (ref to `KitchenStationType.StationTypeId`) | Nullable. `null` = not cooked at a station — picked up from a passive source instead (freezer, bar, pantry) |
| `PickupSourceType` | `string?` | Set only when `RequiredStationType` is null — which passive source this comes from (e.g. `"freezer"`, `"bar"`, `"pantry"`). Preset-driven label, not a hardcoded string in logic |
| `PrepTimeSeconds` | `float` | Base duration, authored in real-world seconds — cook time if station-prepped, fetch/pickup time if pickup-sourced. Converted to `PrepTicks` at load time via `TicksPerRealSecond` (mechanics doc §14), then modified by `KitchenStationType.SpeedMultiplier`/Cook `Speed` when applicable |
| `DefaultPrice` | `decimal` | Doc §4 — player can override via restaurant pricing; this is the shipped default |
| `AvailableMealPeriods` | `IReadOnlyList<string>` | **Added — coherence-check pass.** Which periods (referencing `MealPeriodDefinition.MealPeriodId`, §24a) this item can be ordered in. Enforced the same way as kids-menu enforcement — `PlaceOrderCommand` validation, not a runtime filter elsewhere. `default_diner`'s menu (presets doc) is available across both its active periods (`"lunch"`+`"dinner"`) uniformly for MVP — no per-item restriction until a period-specific item (e.g. a future breakfast dish) actually exists |

**No `RecipeId`/`IngredientRequirements`.** Still correctly excluded — when
Inventory DLC (doc §6) is designed, it extends via a separate
`MenuItemRecipe` lookup table keyed by `MenuItemId`, joined at query time.
Zero schema change to `MenuItem` when that happens.

**Pickup items aren't free, just station-less.** `PrepTimeSeconds` still
applies (fetch time, not zero). **Corrected in coherence-check pass:**
`PickupSourceType`'s referenced fixture is not purely a future DLC hook —
the MVP layout (presets doc) already places a real `Freezer` instance (see
new §18a below). What remains DLC-deferred is only the *stock/capacity*
behavior (Inventory system, business logic §6) — the placed entity itself
exists now, `PickupSourceType` just doesn't gate anything against it yet.

---

## 18a. Freezer (passive pickup fixture — added in coherence-check pass)

**Fulfills:** a real target for `MenuItem.PickupSourceType == "freezer"`
pickup-leg pathing (mechanics doc §10's delivery-leg logic needs an actual
`GridPosition` to fetch from) and the presets doc's MVP layout, which
already places one — this entity was missing from the model despite being
instantiated as real content.

| Field | Type | Notes |
|---|---|---|
| `FreezerId` | `int` | Identity |
| `Position`, `Hitbox` | via `IPlaceable` | Same shared contract as `Table`/`KitchenStation`/`HeatLamp`; MVP fixed layout |

No stock/capacity fields — those arrive with Inventory DLC (business logic
§6) as an additive extension, same "add fields later, don't guess now"
treatment already applied to `MenuItem`'s recipe fields. `PickupSourceType`
values other than `"freezer"` (e.g. `"bar"`, `"pantry"`) have no backing
entity yet — only `Freezer` is real for MVP, matching the presets doc's
actual layout content.

---

## 19. Restaurant (aggregate root)

One instance per active session, per the Architecture chat's multi-instance/
multi-tenant model. Structured as a thin composition root over cohesive
sub-state groups, **not** a flat bag of every list in the system — a flat
shape would force any module or DLC feature to depend on the whole aggregate
just to touch one part of it, which works against the "reusable engine" and
independent-module goals.

| Field | Type | Notes |
|---|---|---|
| `RestaurantId` | `Guid` | Identity, ties into per-session SignalR groups |
| `ConfigId` | `string` | Ref to the `RestaurantConfig` preset this session started from |
| `SimulationTick` | `long` | Authoritative clock |
| `CurrentDayNumber` | `int` | **Added — coherence-check pass.** Increments on each `DayEnded` (mechanics doc §15). Feeds `StatisticsSnapshot`'s per-day framing and the future seasonality DLC's day-counting (business logic §6) |
| `Cash` | `decimal` | Doc §4 |
| `Reputation` | `float (0–100)` | Doc §1.1/§4 — economic multiplier; raised/lowered by service quality, inspections, critics, complaints |
| `Floor` | `FloorState` | `Tables`, `Clusters`, `KitchenStations`, `HeatLamps`, `Bathrooms` — physical layout. **`Bathrooms` added in concurrency-sweep session** (see new §24 entity below) |
| `StaffRoster` | `StaffRosterState` | `Staff` (same `IReadOnlyList<StaffMember>` as before, just relocated for cohesion) |
| `CustomerService` | `CustomerServiceState` | `Queue`, `ActiveGroups`, `PendingCalls`, `Reservations` |
| `KitchenOps` | `KitchenOpsState` | `ActiveOrders`, `ActiveTickets` |
| `Events` | `EventState` | `ActiveEvents`, `ComplaintLog` |
| `AutoManagerToggles` | `Dictionary<AutoManagerSystem, bool>` | **Added in concurrency-sweep session.** Per-system on/off state (seating, waiter-actions/jokes, Busser task-priority, etc.) — session-mutable, defaults all-on, persists across the session (not a `RestaurantConfig` preset value, since the player can flip it live). MES is deliberately **not** a toggleable entry — always automated regardless of this state; the player's only lever over MES is the per-ticket priority bump (mechanics doc §10). `AutoManagerSystem (enum: Seating, WaiterActions, TaskPriority)` — one member per toggleable strategy interface in architecture doc §5 (`ISeatingStrategy`, `IWaiterActionStrategy`, `IStaffTaskPriorityStrategy`); `IKitchenSyncStrategy`/MES excluded, per the rule stated in this same row |

**Sub-state groups are not new entities** — each is a thin wrapper bundling
lists that already existed on the flat draft (e.g. `Floor.Tables` is the same
`IReadOnlyList<Table>` as before). Purely a structural regrouping for
cohesion and module-isolation, no new data introduced.

**`SnapshotHistory` deliberately excluded.** Per `ISnapshotStore`
(architecture doc §7.5) — stats are written *out* to a store, not
accumulated on the hot per-tick aggregate.

**Frontend note (non-binding on this model):** this grouping happens to map
closely to a tabbed UI (main room ≈ `Floor` + `CustomerService`, kitchen tab ≈
`KitchenOps`, stats tab ≈ snapshot store) — a good sign the split is
cohesive, but the actual tab/window/popup layout is entirely a frontend
concern, not modeled here.

---

## 20. Reservation

Doc §1.1 scope, MVP-minimal — edge cases (no-show handling, overbooking risk)
correctly deferred to DLC per doc §6.

| Field | Type | Notes |
|---|---|---|
| `ReservationId` | `int` | Identity |
| `ScheduledTime` | `long` (tick) | When the party is expected |
| `PartySize` | `int` | |
| `GroupProfile` | pre-generated group profile data (not yet a live `CustomerGroup`) | Doc §1.1: profile is generated at booking time, before anyone has arrived — no `CustomerGroup` exists yet to reference, so the profile data is carried directly on the reservation and used to spawn a real group at arrival |
| `AssignedClusterId` | `int?` | Optionally pre-assigned at booking; MVP can leave this to resolve at arrival via normal seating logic instead |
| `GraceWindowTicks` | `int` | How long past `ScheduledTime` the hold persists before being releasable |
| `Status` | `enum: Booked, Arrived, Released` | `Released` = grace window expired, host freed the hold (small trust/reputation penalty per doc §1.1) |

**Closes a gap:** while a reservation actively holds a table, that cluster's
derived `State` reads `Reserved` — giving that enum value (present since
`TableCluster` was first drafted) something to actually represent.

**Gap found during concurrency-sweep doc-consistency check:** now that
`State` is fully derived (see `TableCluster` above), the `Reserved` branch
needs an actual query to derive from — `TableCluster` has no stored backlink
to "which reservation is holding me." Resolved the same way as everything
else derived in this doc (query over stored backlink, matching `IsMerged`'s
pattern): `Reserved` = any `Reservation` exists where
`AssignedClusterId == this ClusterId` and `Status == Booked` and the current
tick falls within its hold window (`ScheduledTime` through
`ScheduledTime + GraceWindowTicks`).

---

## 21. RestaurantEvent

Doc §5's event framework — explicitly designed to be extensible (future:
health scare, supply shortage, power outage). Event *types* are data
(`EventType` + preset config), not subclasses, matching every other
variation point in this model (`TableShape`, `KitchenStationType`).

| Field | Type | Notes |
|---|---|---|
| `EventId` | `int` | Identity |
| `EventType` | `string` | **Corrected — coherence-check pass.** Was a closed C# `enum` (`Inspection, FoodCritic, Celebrity`) with "future types are new enum members" as its own stated extension path — the exact same problem just fixed for `MealPeriodType`→`MealPeriodId`, and inconsistent with `RestaurantConfig.EventModuleIds` (§22) already being a string list for the same DLC-module-identification purpose. Preset key now (e.g. `"inspection"`, `"food_critic"`, `"celebrity"`) — a DLC type (e.g. `"power_outage"`) is a new string value only; the `IEventResolutionStrategy`/`ISimulationModule` dictionaries (architecture doc §5.5) already key by this value, so nothing about the registration mechanism needed to change, only the field's own type |
| `TriggeredAtTick` | `long` | Applies to all types — when it started |
| `Status` | `enum: Active, Resolved` | Applies to all types. **Who flips it — added, coherence-check pass:** resolved by `IEventResolutionStrategy` (architecture doc §5.5), one implementation per `EventType`, registered outside `Domain`'s core tick logic — not hardcoded per-type inside this entity or the sim loop. This is what lets a DLC-added `EventType` define an arbitrary resolution condition (a new strategy class) without any core edit, the same way `ISimulationModule` already lets a DLC type define arbitrary *effects* without one |
| `Data` | type-specific payload | See below — differs meaningfully per type, not one bag of nullable fields shared by all |

**Revised — coherence-check pass:** `DurationTicks` was previously on this
shared table, but it's only ever meaningful for `Inspection` —
`FoodCritic`/`Celebrity` have no independent duration at all (see below).
Keeping it on the shared base, even nullable, was inconsistent with the
pattern `Data` already exists for: type-specific fields belong in the
type-specific payload, not as an always-present-but-sometimes-unused field
on the shared record. Moved into Inspection's `Data` payload below.

**Type-specific payloads and their resolution strategy:**
- **Inspection** — `GradeResult` (Pass/Fail/Grade), `FineAmount?`,
  `DurationTicks` (genuine processing time for the grading/checklist pass,
  derived from `InspectionDurationSeconds` per §14 — entity doc §22).
  Resolved by the built-in `DurationBasedResolution` strategy: `Active →
  Resolved` once `CurrentTick ≥ TriggeredAtTick + Data.DurationTicks`,
  tick-scheduled like any other timed transition (mechanics doc §8).
  Cooldown ("no new Inspection until N days after resolution") is
  enforced at the `EventState` sub-state level, not per-event — it's a
  restaurant-wide constraint, not something one event instance tracks on
  itself.
- **FoodCritic** / **Celebrity** — see the module pattern below; these don't
  carry event-specific payload fields directly, they operate by tagging a
  spawned `CustomerGroup`. Both behave exactly like a normal customer visit
  (same `CoursePhase`/`LifecycleState` progression, same Mood/Kitchen/Staff
  mechanics) — no independent duration to track at all. Resolved by the
  built-in `CustomerGroupBoundResolution` strategy: `Active → Resolved`
  once the tagged group's `LifecycleState` reaches `Leaving`/gone, rather
  than the event instance timing itself. No bespoke commands for either
  in MVP; visible differentiation (particle effects, etc.) is purely a
  frontend/UI concern, not modeled here. `FoodCriticModule`'s
  reputation-swing formula is applied on that same `GroupLeft` (already
  correct below) — i.e. **after** the critic's visit concludes, matching
  a real review being written post-meal.
- **A future DLC type** (e.g. power outage) supplies its own `Data`
  payload shape and its own `IEventResolutionStrategy` implementation
  (e.g. resolved when affected stations are restored) — no edit to this
  entity, the two built-in strategies above, or the sim loop that invokes
  them, required.

**Self-suppression rule** (same-type stacking harder while one is active,
weakens at high reputation) — computed by filtering `ActiveEvents` by
`EventType`, not stored on any individual event. **Independent of
resolution strategy and independent of other event types** — self-
suppression only ever compares same-`EventType` entries against each
other; it has no cross-type exclusivity (an Inspection and a Celebrity can
be `Active` simultaneously, nothing in either doc says otherwise). Roll
cadence for *when* each `EventType` gets a trigger chance is **not yet
locked anywhere** — flagged as an open decision, not something this
session invented an answer for.

---

## Extension pattern: independent DLC modules (Critic, Celebrity, and future types)

**Rejected approach:** dedicated bools on `CustomerGroup` (`IsCritic`,
`IsCelebrity`). Works for exactly two cases, but means every future
special-customer type requires editing `CustomerGroup`'s core schema again —
the opposite of independent, Sims-4-style expansions, and it forces the base
entity to know about DLC concepts in advance.

**Adopted approach — generic tag on `CustomerGroup`:**

| Field | Type | Notes |
|---|---|---|
| `SpecialRole` | `string?` | Open-ended tag, e.g. `"critic"`, `"celebrity"` — not an enum baked into core. **Casing corrected, coherence-check pass** — was `"Critic"`/`"Celebrity"`, inconsistent with the lowercase snake_case convention used for `EventType`/`MealPeriodId` |
| `SpecialRoleData` | `Dictionary<string, float>?` | Optional payload the owning module reads (e.g. Critic's scrutiny multiplier, Celebrity's buzz radius) |

Core `CustomerGroup` logic (arrival, ordering, Mood, lifecycle) never branches
on `SpecialRole` — it's inert data as far as the base game is concerned. Each
module is fully self-contained:

- `FoodCriticModule : ISimulationModule` — subscribes to `GroupSpawned`,
  decides independently whether to tag a group `"critic"` and adjust its
  starting `Mood`/`PatienceLevel`; subscribes to `GroupLeft` to apply its own
  larger reputation-swing formula by checking the tag.
- `CelebrityModule` — same shape, entirely independently. Reputation-scaled
  spawn chance and (future) a Mood/Patience boost for groups arriving while
  one is present, both owned by this module alone.

Adding a fourth special-customer type later (health inspector undercover, food
blogger, etc.) means writing a new module and registering it — zero edits to
`CustomerGroup`, `RestaurantEvent`, or any other core file. This is the actual
mechanism that makes "expansion pack" -style independence real, not just
aspirational.

**Complaints loop now closes:** `ComplaintRecord`'s accumulated severity
(§11) feeds directly into `Inspection`'s trigger-probability roll — the
connection that was missing until `RestaurantEvent`/`Inspection` existed to
receive it.

---

## 22. RestaurantConfig

The root preset — referenced by `Restaurant.ConfigId`, loaded once at session
start via `IPresetRepository<RestaurantConfig>`. This is the entity that
makes the whole system preset-agnostic: a new restaurant scenario is a new
JSON file, zero code.

| Field | Type | Notes |
|---|---|---|
| `ConfigId` | `string` | Preset key, e.g. `"default_diner"` |
| `StartingCash` | `decimal` | |
| `MinutesPerTick` | `int` | Sim-time scale, per Architecture chat |
| `TicksPerRealSecond` | `int` | Ticks executed per real-world second (mechanics doc §14) — every duration-bearing preset value below is authored in real-world seconds and converted to ticks via this value at load time |
| `TableLayoutId` | `string` | Ref to a separate layout preset defining `Table`/`KitchenStation`/`HeatLamp` starting positions — kept out of this entity so layouts can be reused across multiple configs |
| `StaffRosterSeed` | `IReadOnlyList<(StaffRole Role, int Count)>` | Fixed MVP starting roster |
| `MenuItemIds` | `IReadOnlyList<int>` | Which `MenuItem` presets this restaurant offers — lets multiple restaurants share one global menu catalog while each exposing a different subset |
| `ReputationStart` | `float` | |
| `PatienceDecayBaseline` | `float` | Base tuning knob feeding per-customer `PatienceLevel` effects (doc §1.1) |
| `QueueWaitMoodDecayRate` | `float` | **Added — coherence-check pass.** Base per-tick Mood decay while queued, default `-0.4` (mechanics doc §2) |
| `CourseWaitMoodDecayRate` | `float` | **Added — coherence-check pass.** Base per-tick Mood decay while waiting on a course, default `-0.3` (mechanics doc §2) |
| `NaturalMoodRecoveryRate` | `float` | **Added — coherence-check pass.** Base per-tick Mood recovery while seated/eating, default `+0.5` (mechanics doc §2) |
| `ProbabilityCheckpointIntervalSeconds` | `float` | **Added — coherence-check pass.** Interval between probability-roll checkpoints (complaint, filing, inter-group spread), default 3 seconds, converted to ticks per §14 (mechanics doc §7) |
| `PathReplanMaxWaitSeconds` | `float` | **Added — coherence-check pass.** Max wait before a stalled path triggers a full replan, default 5 seconds, converted to ticks per §14 (mechanics doc §9) |
| `MaxCallAttempts` | `int` | **Added — coherence-check pass.** Cap on `CustomerCall.AttemptCount` (§10), default `3` |
| `ActiveMealPeriods` | `IReadOnlyList<MealPeriodDefinition>` | **Added — coherence-check pass.** Which meal periods (§24a) this restaurant operates, with their hours. Gates Arrival spawning (business logic §1.1) and staff scheduling (§6, §24a) — no active period covering the current time means no arrivals and no staff clocked in |
| `EventModuleIds` | `IReadOnlyList<string>` | Which `ISimulationModule`s are active for this restaurant — ties directly into the module-independence pattern from `RestaurantEvent`. Even *which DLC modules run* is per-restaurant data, not a global switch: one config can enable `FoodCriticModule`, another can omit it, with no code change either way |
| `ShortDelayThresholdSeconds` | `float` (seconds) | Heat-lamp-vs-reinform base threshold before `WaitPressure` scaling, converted to `ShortDelayThresholdTicks` at load time (mechanics doc §10, §14) |
| `MaxAcceptableWaitSeconds` | `float` (seconds) | Denominator for `WaitPressure` in the heat-lamp decision, converted to `MaxAcceptableWaitTicks` at load time (mechanics doc §10, §14) |
| `BaseHighQualityChance` | `float` | Base coefficient in the `IsHighQuality` roll (mechanics doc §10) |
| `BaseEventChance` | `float` | Base coefficient in the `FoodQualityEvent` roll (mechanics doc §10) |
| `PlatingTimeSeconds` | `float` (seconds) | Flat cook→pass-point transfer time before an item is pickup-eligible, converted to `PlatingTicks` at load time (mechanics doc §10, §14) |
| `TipBaseRate` | `float` | Base coefficient in the tip formula, default `50` (mechanics doc §6) |
| `TipMoodExponent` | `float` | Exponent applied to `Mood/100` in the tip formula, default `0.75` (mechanics doc §6) |
| `AutoJokeCooldownSeconds` | `float` | Per-table cooldown between autonomous AutoManager jokes (mechanics doc §11) |
| `MoodGapThreshold` | `float` | Mood-gap that triggers AutoManager to target one person vs. the whole table for a joke (mechanics doc §11) |
| `FacilitiesTimeSecondsMale`, `FacilitiesTimeSecondsFemale` | `float` | Gendered `UseFacilities` sub-step duration (mechanics doc §12) |
| `BaseBathroomVisitChance` | `float` | **Added — coherence-check pass.** Per-checkpoint base probability a present customer decides to go (mechanics doc §12). Open, pending playtesting |
| `BathroomCooldownSeconds` | `float` | **Added — coherence-check pass.** Minimum time since a customer's last visit ended before they're eligible again, converted to ticks per §14 (mechanics doc §12). Open, pending playtesting |
| `EatingMealtimeVisitModifier` | `float` | **Added — coherence-check pass.** Multiplier (< 1) applied to the visit-trigger chance while a customer is actively eating a served course, vs. the normal rate while waiting (mechanics doc §12). Open, pending playtesting |
| `WashHandsTimeSecondsMale`, `WashHandsTimeSecondsFemale` | `float` | Gendered `WashHands` sub-step duration (mechanics doc §12) |
| `AlmostDirtyThreshold`, `DirtyThreshold` | `float` | Two-tier bathroom cleanliness thresholds (mechanics doc §12) |
| `TableCleaningBaseSeconds` | `float` | Base Busser table-cleaning duration before speed/fatigue scaling (mechanics doc §13) |
| `BathroomCleaningBaseSeconds` | `float` | Base Busser bathroom-cleaning duration before speed/fatigue scaling (mechanics doc §13) |
| `ComplaintCategoryWeights` | `Dictionary<ComplaintCategory, float>` | Base severity weight per `ComplaintRecord.Category` (doc §11) — data-driven, tunable per restaurant type without touching code |
| `InspectionDurationSeconds` | `float` | **Added — coherence-check pass, renamed from an earlier draft.** Authored duration of the Inspection grading/checklist pass, converted to `DurationTicks` on the event's `Data` payload at trigger time via §14. Open, pending playtesting — same treatment as `TableCleaningBaseSeconds`/`BathroomCleaningBaseSeconds`, no separate "max" needed unless a reason for variability is designed later |

---

## 23. StatisticsSnapshot

Per architecture doc §7.5: a `struct` (value type), stored in a rolling log
via `ISnapshotStore` — never held on the hot `Restaurant` aggregate itself,
to avoid heap churn on something created every N ticks.

| Field | Type | Notes |
|---|---|---|
| `Tick` | `long` | When this snapshot was taken |
| `Revenue` | `decimal` | Period revenue |
| `CustomersServed` | `int` | |
| `AvgWaitTicks` | `float` | Queue + course-wait average |
| `TableUtilization` | `float` | % of clusters occupied |
| `AvgFinalMood` | `float` | Aggregate of `Customer.Mood` at payment across the period — the stats-facing view of an otherwise per-customer, ephemeral value |
| `ComplaintCount` | `int` | From `ComplaintLog`, period total |
| `ActiveEventTypes` | `IReadOnlyList<string>` | Which `RestaurantEvent`s were active during this window, by `EventType` string key |
| `TotalGameplayDays` | `int` | **Added — coherence-check pass.** `Restaurant.CurrentDayNumber` (§19) captured at snapshot time — lets a stats view chart by actual day count rather than raw `Tick`, which drifts if `TicksPerRealSecond` ever changes (mechanics doc §14) |
| `Costs` | `decimal` | **Added in coherence-check pass.** Period total of wages, cleaning fee, restock, maintenance, utilities, breakage/waste — needed for the locked-MVP day-end summary (business logic §4), previously unrepresented on this struct |
| `NetProfit` | `decimal` (derived: `Revenue - Costs`) | **Added in coherence-check pass.** Not stored independently — computed from the two fields above at read time, same derivation pattern as `TableCluster.IsMerged` |
| `ReputationDelta` | `float` | **Added in coherence-check pass.** Net reputation change over the period — required by business logic §4's day-end summary |

**Deliberately minimal for MVP.** Doc §6 describes a full statistics window
with time-series across *all* metrics as a DLC feature; this shape covers
core-loop metrics only. Extending later is additive — new fields on the
struct — since `ISnapshotStore` (architecture doc §7.5) already abstracts
persistence.

---

## 24. Bathroom (added in concurrency-sweep session)

Two customer-facing instances (Male, Female) plus one separate unisex staff
bathroom. Uses the shared `IPlaceable` contract like `Table`/`KitchenStation`
/`HeatLamp` — but per the "doors only" MVP scope, `Position` is just a door
tile with one adjacent standing/queue slot, not a real interior room object.
Customers visually "vanish" on entry — a rendering-only concern, not modeled
here.

| Field | Type | Notes |
|---|---|---|
| `BathroomId` | `int` | Identity |
| `Gender` | `enum: Male, Female, Unisex` | `Unisex` = the staff bathroom, which has no Mood interaction — purely an availability/hygiene blocker for `StaffMember` |
| `Position`, `Hitbox` | via `IPlaceable` | Door tile + one adjacent slot, per MVP's "doors only" scope |
| `Capacity` | `int` | Concurrent occupants |
| `OccupiedBy` | `IReadOnlyList<CustomerId>` (or `StaffId` for the unisex one) | Current occupants, up to `Capacity` |
| `CleanlinessScore` | `float (0–100)` | Customer bathrooms only — decays by chance on each use (mechanics doc §12), reset by Busser cleaning (§13) or end-of-day processing |
| `IsClean` | `bool` (derived: `CleanlinessScore >= DirtyThreshold`) | Not stored — same derivation pattern as `TableCluster.State` |
| `MaxConcurrentCleaners` | `int` | **Added — coherence-check pass.** Caps how many Bussers can usefully work this bathroom at once (a small door-only bathroom needs fewer than a large one) — used only by a tuned/DLC `IStaffTaskPriorityStrategy` that respects it (mechanics doc §13); MVP's default strategy may ignore it. Size-scaled default open, pending playtesting |

**Queue:** a FIFO wait queue per bathroom, gender-specific for the customer
ones — same shape as `HeatLamp`'s fixed-slot-capacity pattern, just with a
queue in front of it instead of the request simply failing when full.
`Customer.IsPresentAtTable = false` the moment they leave the table to
queue, not just once actually inside — the customer is "away" for the whole
duration, not only the active-use portion.

**Two-tier cleanliness threshold** (`AlmostDirtyThreshold` > `DirtyThreshold`,
mechanics doc §12): mirrors the heat-lamp/re-inform pattern of giving a
decision window — a bathroom becomes Busser-eligible for cleaning before it
actually starts costing customers Mood, rather than a single hard cutoff.

**Staff bathroom is unisex, deliberately not split like the customer ones**
— its whole role is a brief hygiene/break availability blocker on
`StaffMember`, not a Mood-driving customer experience; splitting it by
gender would add bookkeeping with no corresponding gameplay payoff.

---

## 24a. MealPeriodDefinition (preset/data — added, coherence-check pass)

**Why it exists:** closes a real gap found during coherence review —
`StaffMember.Fatigue` is documented everywhere as "rising over a shift,"
but no shift/day boundary existed anywhere in the model. Real restaurants
also don't run one continuous undifferentiated service block — they have
named periods (breakfast, lunch, the afternoon lull, dinner) with their
own hours, their own menu subset, and their own staffing. This entity
gives "shift" a real, data-driven meaning instead of an implicit one.

Same pattern as `TableShape`/`KitchenStationType`: variation is data, not
subclasses.

**Corrected — same session.** Originally specified `MealPeriodType` as a
closed C# `enum`, which would have meant a new DLC meal period requires
editing and recompiling `Domain` — exactly the problem `TableShape`/
`KitchenStationType`'s string-key pattern exists to avoid, and stricter
than what `RestaurantEvent` needs (a meal period drives only equality/
time-window/subset checks, never type-specific *behavior*, so unlike
`RestaurantEvent` it needs no compiled strategy class at all — pure data
is genuinely sufficient here). Fixed below to match the established
pattern exactly.

| Field | Type | Notes |
|---|---|---|
| `MealPeriodId` | `string` | Preset key (e.g. `"breakfast"`, `"lunch"`, `"afternoon"`, `"dinner"`) — same pattern as `TableShape.ShapeId`/`KitchenStationType.StationTypeId`. A DLC meal period (e.g. `"happy_hour"`, `"private_event"`) is a new preset entry only — no enum member, no new class, no recompiled assembly. Genuinely data-only: a DLC pack containing just new `MealPeriodDefinition`/`MenuItem`/roster-assignment JSON works without shipping any compiled code at all, since nothing branches on *which* period this is anywhere in the model — only on its time window and which items/staff reference its `MealPeriodId` |
| `StartTime`, `EndTime` | `TimeOfDay (int Hour, int Minute)` | Time-of-day window, e.g. Lunch 12:00–15:00. Derived against `Restaurant.SimulationTick` via `MinutesPerTick` (entity doc §22) — `TicksPerDay = round(1440 / MinutesPerTick)`, `TimeOfDayTicks = SimulationTick % TicksPerDay` |

`RestaurantConfig.ActiveMealPeriods: IReadOnlyList<MealPeriodDefinition>`
(entity doc §22, new field) — which periods a given restaurant actually
runs, and their hours. A restaurant configured with only `"lunch"`+
`"dinner"` has no `"breakfast"`/`"afternoon"` window at all — no arrivals
spawn, no staff are scheduled, during those hours (business logic §1.1).
Per-weekday variation (different hours on weekends) is **explicitly
deferred, not designed** — flagged as an open question, not guessed at
here.

**Ties into `MenuItem`** (§18) via a new `AvailableMealPeriods:
IReadOnlyList<string>` field (referencing `MealPeriodId`) — which periods
an item can be ordered in (e.g. a future breakfast-only item wouldn't
appear on a dinner-period order). Enforced the same way kids-menu
enforcement already is: command validation on `PlaceOrderCommand`, not a
stored flag.

**Ties into `StaffMember`** (§6) via a new `AssignedMealPeriods:
IReadOnlyList<string>` field (referencing `MealPeriodId`) — which periods
this staff member is rostered for. Validated at assignment/hire time: must
be a subset of the restaurant's own `ActiveMealPeriods` (a restaurant that
only serves breakfast can't roster a lunch-only staff member).
`StaffMember.State` gains a new value, `OffShift`, alongside the existing
`Idle`/`Busy`/`Resting`. **Corrected — coherence-check pass:** `OffShift`
is bounded by the staff member's **full daily shift span**, not each
individual assigned period — `ShiftStart` = the earliest `StartTime`
among that staff member's `AssignedMealPeriods` active today, `ShiftEnd`
= the latest `EndTime` among them. A staff member assigned to
`["breakfast", "lunch"]` stays `Idle`/`Busy` continuously from Breakfast's
opening through Lunch's close — the gap between the two periods is *not*
a clock-out point, matching how a real employee works through a slow
mid-morning lull without going home and coming back. `OffShift` only
applies before `ShiftStart` or after `ShiftEnd`. **This is what "shift"
now concretely means**: `Fatigue` rises only while a staff member is not
`OffShift`, and resets fully on the `Busy`/`Idle → OffShift` transition
at `ShiftEnd` — once per day, not once per assigned period — closing the
originally-flagged gap. A
mid-`Busy` staff member finishes their current task before clocking out,
same "finish current task before switching" treatment already given to
Bussers (mechanics doc §13).

**MVP default:** `default_diner` (presets doc) runs `"lunch"`+`"dinner"` only
— matches a typical sit-down restaurant, per the explicit call to keep
the MVP preset simple even though the underlying system supports all four
periods plus DLC extension.

---

## Entity pass complete (25/24 — Freezer added in coherence-check pass;
`MealPeriodDefinition` added at §24a in the same pass as an additional,
uncounted preset-data entity, same treatment as `TableShape`/
`KitchenStationType`)


All entities from the MVP data model are specified above, cross-checked
against `restaurant-simulator-business-logic.md` and reconciled with prior
Architecture-chat decisions, including the concurrency-sweep session's
additions (`Table.IsClean`, derived `TableCluster.State`, `Customer`'s three
new fields, `TableCluster` joke-fatigue fields, the `Bathroom` entity, and
`Restaurant.AutoManagerToggles`). Extension points confirmed open for every
deferred DLC system (Inventory, recipes, ambiance, hiring, marketing,
seasonality, takeout) without requiring schema rework to core entities.

Not yet done, by design: agent files (explicitly scoped to a separate,
later chat).
