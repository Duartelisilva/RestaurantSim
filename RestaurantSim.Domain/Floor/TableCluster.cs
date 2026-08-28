namespace RestaurantSim.Domain.Floor;

public enum TableClusterState
{
    Free,
    Reserved,
    Occupied,
    NeedsCleaning
}

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

    // Phase 1: stub only. Real derivation (NeedsCleaning/Occupied/Reserved/Free
    // priority order per entity doc §5) is business logic, added in a later phase.
    public TableClusterState State { get; set; } = TableClusterState.Free;
}
