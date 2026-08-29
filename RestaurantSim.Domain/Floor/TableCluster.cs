using RestaurantSim.Domain.CustomerService;
using System.Linq;

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
}
