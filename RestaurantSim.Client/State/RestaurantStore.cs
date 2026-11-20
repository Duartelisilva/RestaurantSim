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

    // Tables currently sliding, by table id — what the floor view animates. Client-only: a
    // fresh snapshot (join / reconnect) shows tables where they are, with no animation.
    public IReadOnlyDictionary<int, TableMotion> Motions { get; private set; } = new Dictionary<int, TableMotion>();

    public event Action? Changed;

    public void Load(RestaurantSnapshotDto snapshot)
    {
        Snapshot = snapshot;
        Motions = new Dictionary<int, TableMotion>();
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
            active.Add(new ActiveGroupDto(
                e.GroupId, queued.PartySize, e.ClusterId, "Seated", "Seated",
                DiningStageNames.Ordering, DiningVisitNames.Drinks));

        Commit(s with
        {
            Floor = s.Floor with { Clusters = Recompute(clusters, s.Floor.Tables) },
            Queue = queue,
            ActiveGroups = active
        });
        return true;
    }

    // Server-computed stage, absolute — set, never toggled. Like GroupSeated, a group the
    // client never saw seated is not invented here (its PartySize is unknown).
    public bool Apply(GroupDiningStageChangedDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        if (s.ActiveGroups.All(g => g.GroupId != e.GroupId))
            return true;

        Commit(s with
        {
            ActiveGroups = s.ActiveGroups
                .Select(g => g.GroupId == e.GroupId ? g with { Stage = e.Stage, NextVisit = e.NextVisit } : g)
                .ToList()
        });
        return true;
    }

    // Resulting topology, absolute: the merged ids disappear, the survivor owns all the tables,
    // and the tables that slid are at their new position (and animate from the old one).
    public bool Apply(ClustersMergedDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        var survivor = new ClusterDto(e.ClusterId, e.TableIds, e.Capacity, "Free", null);
        var clusters = s.Floor.Clusters
            .Where(c => c.ClusterId != e.ClusterId && !e.MergedClusterIds.Contains(c.ClusterId))
            .Append(survivor);

        Commit(WithTopology(s, clusters, e.TableIds.Select(id => (id, e.ClusterId)), e.Moves, e.DurationTicks));
        return true;
    }

    // The merged cluster is replaced by the original clusters, tables sliding back home.
    public bool Apply(ClusterSplitDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        var restored = e.Restored.Select(r => new ClusterDto(r.ClusterId, r.TableIds, r.Capacity, "Free", null)).ToList();
        var restoredIds = restored.Select(r => r.ClusterId).ToHashSet();
        var clusters = s.Floor.Clusters
            .Where(c => c.ClusterId != e.MergedClusterId && !restoredIds.Contains(c.ClusterId))
            .Concat(restored);

        var owners = e.Restored.SelectMany(r => r.TableIds.Select(id => (id, r.ClusterId)));
        Commit(WithTopology(s, clusters, owners, e.Moves, e.DurationTicks));
        return true;
    }

    // The cluster's tables have stopped sliding: it is usable again.
    public bool Apply(TableMoveCompletedDto e)
    {
        if (!TryGetCurrent(e.Tick, out var s))
            return false;

        var cluster = s.Floor.Clusters.FirstOrDefault(c => c.ClusterId == e.ClusterId);
        if (cluster is null)
            return true;

        var tables = s.Floor.Tables
            .Select(t => cluster.TableIds.Contains(t.TableId) ? t with { IsMoving = false } : t)
            .ToList();

        Commit(s with { Floor = s.Floor with { Tables = tables, Clusters = Recompute(s.Floor.Clusters, tables) } },
            Motions.Where(m => !cluster.TableIds.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value));
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

    private void Commit(RestaurantSnapshotDto next, IReadOnlyDictionary<int, TableMotion> motions)
    {
        Motions = motions;
        Commit(next);
    }

    // Shared by merge and split: new clusters, which cluster owns which table, and the slides.
    // A table that slides is Moving (so its cluster derives as Moving) until TableMoveCompleted.
    private RestaurantSnapshotDto WithTopology(
        RestaurantSnapshotDto s,
        IEnumerable<ClusterDto> clusters,
        IEnumerable<(int TableId, int ClusterId)> owners,
        IReadOnlyList<TableMoveDto> moves,
        long durationTicks)
    {
        var ownerOf = owners.ToDictionary(o => o.TableId, o => o.ClusterId);
        var moveOf = moves.ToDictionary(m => m.TableId);

        var tables = s.Floor.Tables.Select(t =>
        {
            var next = ownerOf.TryGetValue(t.TableId, out var clusterId) ? t with { ClusterId = clusterId } : t;
            return moveOf.TryGetValue(t.TableId, out var move) ? next with { Position = move.To, IsMoving = true } : next;
        }).ToList();

        var seconds = durationTicks / (double)Math.Max(1, s.TicksPerRealSecond);
        var motions = Motions.Where(m => !moveOf.ContainsKey(m.Key)).ToDictionary(m => m.Key, m => m.Value);
        foreach (var move in moves)
            motions[move.TableId] = new TableMotion(move.From, move.To, seconds);

        Motions = motions;
        var ordered = clusters.OrderBy(c => c.ClusterId).ToList();
        return s with { Floor = s.Floor with { Tables = tables, Clusters = Recompute(ordered, tables) } };
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
        if (tables.Any(t => cluster.TableIds.Contains(t.TableId) && t.IsMoving))
            return "Moving";

        var needsCleaning = tables.Any(t => cluster.TableIds.Contains(t.TableId) && !t.IsClean);
        if (needsCleaning)
            return "NeedsCleaning";

        return cluster.OccupiedByGroupId is not null ? "Occupied" : "Free";
    }
}
