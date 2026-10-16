using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.State;

// Client-side copy of the restaurant, built from one snapshot plus pushed events.
// Pure state logic — no SignalR, no rendering — so it is unit-testable and stays
// valid whichever engine hosting (server or in-process) is chosen later.
//
// Event rule (see RestaurantHub): drop events with Tick < snapshot tick, apply
// equal-tick events idempotently (set state, never toggle).
public sealed class RestaurantStore
{
    public RestaurantSnapshotDto? Snapshot { get; private set; }

    public event Action? Changed;

    public void Load(RestaurantSnapshotDto snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke();
    }

    // Returns true if the event was applied, false if it was ignored.
    public bool Apply(GroupSeatedDto e)
    {
        var s = Snapshot;
        if (s is null || e.Tick < s.SimulationTick)
            return false;

        var clusters = s.Floor.Clusters
            .Select(c => c.ClusterId == e.ClusterId
                ? c with { State = "Occupied", OccupiedByGroupId = e.GroupId }
                : c)
            .ToList();

        // PartySize only exists on the queued entry. A group that arrived after
        // the snapshot is unknown here (GroupArrived is not pushed yet), so it
        // occupies its cluster but is not added to ActiveGroups.
        var queued = s.Queue.FirstOrDefault(g => g.GroupId == e.GroupId);
        var queue = s.Queue.Where(g => g.GroupId != e.GroupId).ToList();
        var active = s.ActiveGroups.ToList();
        if (queued is not null && active.All(g => g.GroupId != e.GroupId))
            active.Add(new ActiveGroupDto(e.GroupId, queued.PartySize, e.ClusterId, "Seated", "Seated"));

        Snapshot = s with
        {
            Floor = s.Floor with { Clusters = clusters },
            Queue = queue,
            ActiveGroups = active
        };

        Changed?.Invoke();
        return true;
    }
}
