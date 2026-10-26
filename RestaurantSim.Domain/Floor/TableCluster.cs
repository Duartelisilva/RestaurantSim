using RestaurantSim.Domain.CustomerService;

namespace RestaurantSim.Domain.Floor;

public enum TableClusterState
{
    Free,
    Reserved,
    Occupied,
    NeedsCleaning
}

// What a runtime merge combined — enough to put the clusters back exactly as they were
// ("return to their normal state") once the merged table has been used and cleaned.
public sealed record ClusterComposition(
    int ClusterId,
    IReadOnlyList<int> TableIds,
    int Capacity,
    int? AssignedWaiterId,
    bool ManuallyAssigned);

public sealed class TableCluster
{
    public required int ClusterId { get; init; }
    public required IReadOnlyList<int> TableIds { get; set; }
    public required int Capacity { get; set; }

    // GroupId of the currently seated group, null otherwise.
    public int? OccupiedBy { get; set; }

    // StaffId of the serving waiter — replaces a static "Section" field.
    public int? AssignedWaiterId { get; set; }
    public bool ManuallyAssigned { get; set; }
    public GridPosition AnchorPosition { get; set; }

    // Feeds JokeFatigueMultiplier (mechanics doc §11). Resets on NeedsCleaning -> Free.
    public int RecentJokeCount { get; set; }
    public long LastJokeTick { get; set; }

    public bool IsMerged => TableIds.Count > 1;

    // Set only on a cluster created by a runtime merge (never on standalone clusters or
    // ones authored pre-merged in the layout, which are that table group's normal state).
    // Flattened: always the ORIGINAL clusters, even if merged clusters were merged again.
    public IReadOnlyList<ClusterComposition>? MergedFrom { get; set; }

    public bool IsMergedOnDemand => MergedFrom is not null;

    // Derived per entity doc §5's priority order: NeedsCleaning > Occupied > Reserved > Free.
    public TableClusterState DeriveState(
        IReadOnlyList<Table> allTables,
        IReadOnlyList<Reservation> reservations,
        long currentTick)
    {
        var memberTables = allTables.Where(t => TableIds.Contains(t.TableId));
        if (memberTables.Any(t => !t.IsClean))
            return TableClusterState.NeedsCleaning;

        if (OccupiedBy is not null)
            return TableClusterState.Occupied;

        var isReserved = reservations.Any(r =>
            r.AssignedClusterId == ClusterId &&
            r.Status == ReservationStatus.Booked &&
            currentTick >= r.ScheduledTime &&
            currentTick <= r.ScheduledTime + r.GraceWindowTicks);

        return isReserved ? TableClusterState.Reserved : TableClusterState.Free;
    }

    // Single source of truth for "can this group be seated here" — called by
    // BOTH SeatGroupCommandValidator and SeatGroupCommandHandler. Previously
    // each independently computed "DeriveState(...) == Free && Capacity >=
    // partySize" inline, which is the same duplicated-logic risk PlaceOrder's
    // refactor addressed — fixed here for consistency across the codebase.
    public bool CanSeat(
        int partySize,
        IReadOnlyList<Table> allTables,
        IReadOnlyList<Reservation> reservations,
        long currentTick)
    {
        return DeriveState(allTables, reservations, currentTick) == TableClusterState.Free
            && Capacity >= partySize;
    }
}
