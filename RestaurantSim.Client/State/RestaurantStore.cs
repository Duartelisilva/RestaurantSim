using System.Diagnostics.CodeAnalysis;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.State;

// Client-side copy of the restaurant, built from one snapshot plus pushed events.
// Pure state logic — no SignalR, no rendering — so it is unit-testable and stays
// valid whichever engine hosting (server or in-process) is chosen later.
//
// Event rule (see RestaurantHub): drop events with Tick < snapshot tick, apply
// equal-tick events idempotently (set state, never toggle). Every Apply returns
// false only when the event was ignored (no snapshot yet, or too old).
public sealed class RestaurantStore
{
    public RestaurantSnapshotDto? Snapshot { get; private set; }

    public event Action? Changed;

    public void Load(RestaurantSnapshotDto snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke();
    }

    public bool Apply(GroupArrivedDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        var known = s.Queue.Any(g => g.GroupId == e.GroupId) || s.ActiveGroups.Any(g => g.GroupId == e.GroupId);
        var queue = known ? s.Queue : s.Queue.Append(new QueuedGroupDto(e.GroupId, e.PartySize)).ToList();

        Commit(s with { Queue = queue });
        return true;
    }

    public bool Apply(GroupAbandonedQueueDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        Commit(s with
        {
            Queue = s.Queue.Where(g => g.GroupId != e.GroupId).ToList(),
            Reputation = e.Reputation
        });
        return true;
    }

    // Cash and Reputation arrive as absolute values after the change, never
    // deltas, so a duplicate or replayed event cannot double-apply.
    public bool Apply(GroupTurnedAwayDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        Commit(s with { Reputation = e.Reputation });
        return true;
    }

    public bool Apply(BillPaidDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        Commit(s with { Cash = e.Cash });
        return true;
    }

    public bool Apply(GroupSeatedDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        var clusters = s.Floor.Clusters
            .Select(c => c.ClusterId == e.ClusterId ? c with { OccupiedByGroupId = e.GroupId } : c)
            .ToList();

        // PartySize only exists on the queued entry. A group the client never saw
        // arrive (it arrived before the snapshot but is not queued any more, or
        // the event was missed) occupies its cluster but is not added to
        // ActiveGroups rather than being invented with a made-up size.
        var queued = s.Queue.FirstOrDefault(g => g.GroupId == e.GroupId);
        var queue = s.Queue.Where(g => g.GroupId != e.GroupId).ToList();
        var active = s.ActiveGroups.ToList();
        if (queued is not null && active.All(g => g.GroupId != e.GroupId))
            active.Add(new ActiveGroupDto(e.GroupId, queued.PartySize, e.ClusterId, "Seated", "Seated"));

        Commit(s with
        {
            Floor = s.Floor with { Clusters = Recompute(clusters, s.Floor.Tables) },
            Queue = queue,
            ActiveGroups = active
        });
        return true;
    }

    public bool Apply(GroupLeftDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        // The server marks every table of the cluster dirty when a group leaves.
        var tableIds = s.Floor.Clusters.FirstOrDefault(c => c.ClusterId == e.ClusterId)?.TableIds
            ?? Array.Empty<int>();
        var tables = s.Floor.Tables
            .Select(t => tableIds.Contains(t.TableId) ? t with { IsClean = false } : t)
            .ToList();
        var clusters = s.Floor.Clusters
            .Select(c => c.ClusterId == e.ClusterId ? c with { OccupiedByGroupId = null } : c)
            .ToList();

        Commit(s with
        {
            Floor = s.Floor with { Tables = tables, Clusters = Recompute(clusters, tables) },
            ActiveGroups = s.ActiveGroups.Where(g => g.GroupId != e.GroupId).ToList()
        });
        return true;
    }

    public bool Apply(TableCleanedDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        var tables = s.Floor.Tables
            .Select(t => t.TableId == e.TableId ? t with { IsClean = true } : t)
            .ToList();

        Commit(s with { Floor = s.Floor with { Tables = tables, Clusters = Recompute(s.Floor.Clusters, tables) } });
        return true;
    }

    private bool TryGetCurrent(long eventTick, [NotNullWhen(true)] out RestaurantSnapshotDto? snapshot)
    {
        snapshot = Snapshot;
        return snapshot is not null && eventTick >= snapshot.SimulationTick;
    }

    private void Commit(RestaurantSnapshotDto next)
    {
        Snapshot = next;
        Changed?.Invoke();
    }

    private static List<ClusterDto> Recompute(IReadOnlyList<ClusterDto> clusters, IReadOnlyList<TableDto> tables)
    {
        return clusters.Select(c => c with { State = DeriveState(c, tables) }).ToList();
    }

    // Mirrors TableCluster.DeriveState (entity doc §5) minus the Reserved branch —
    // reservations are not built and not in the snapshot. Duplicated on purpose:
    // the client may not reference Domain. Covered by tests; revisit when
    // Reservations exist.
    private static string DeriveState(ClusterDto cluster, IReadOnlyList<TableDto> tables)
    {
        var needsCleaning = tables.Any(t => cluster.TableIds.Contains(t.TableId) && !t.IsClean);
        if (needsCleaning)
            return "NeedsCleaning";

        return cluster.OccupiedByGroupId is not null ? "Occupied" : "Free";
    }
}
