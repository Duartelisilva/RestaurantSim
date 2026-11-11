namespace RestaurantSim.Domain.Floor;

public enum MergeAxis
{
    Horizontal, // tables are pushed together left-right (seats face North/South)
    Vertical    // tables are pushed together top-bottom (seats face East/West)
}

// A set of whole clusters that could be merged into one, and the capacity it would have.
public sealed record MergePlan(IReadOnlyList<int> ClusterIds, int Capacity);

// Pure geometry of merging (entity doc §2 CanMerge, §4 merge-axis rule, §5 no cap /
// single line only). Both the MergeClustersCommand's validation and the AutoManager's
// seating strategy go through these methods, so "can these tables merge" has exactly one
// definition (CLAUDE.md: no duplicated rules).
//
// Tables are movable (mechanics doc §18, draft): two tables in one lane merge if the gap
// between them is at most MergeGeometry.MaxGapCells and free. Every method takes an optional
// geometry; leaving it out means "tables must already touch", the behaviour before tables
// could move.
public static class TableMergeRules
{
    // The axis along which this table may be joined to a neighbour: perpendicular to
    // the way its seats face. Null if its shape cannot merge or its seats don't agree.
    public static MergeAxis? AxisOf(Table table)
    {
        if (!table.Shape.CanMerge || table.Seats.Count == 0)
            return null;

        if (table.Seats.All(s => s.Orientation is Orientation.North or Orientation.South))
            return MergeAxis.Horizontal;

        if (table.Seats.All(s => s.Orientation is Orientation.East or Orientation.West))
            return MergeAxis.Vertical;

        return null;
    }

    // The most people the layout could EVER seat at one table group: the biggest run of
    // touching, mergeable tables (ignoring who is sitting where right now).
    public static int MaxPartySize(IReadOnlyList<Table> tables, MergeGeometry? geometry = null)
    {
        var best = 0;
        foreach (var line in Lines(tables, geometry))
            best = Math.Max(best, line.Sum(t => t.Seats.Count));

        return best;
    }

    // The cheapest way to seat `partySize` by merging whole, free clusters that sit in a
    // straight touching line: smallest resulting capacity, then fewest clusters, then the
    // lowest cluster id (deterministic). Null if no merge is possible.
    public static MergePlan? FindMerge(
        int partySize,
        IReadOnlyList<Table> tables,
        IReadOnlyList<TableCluster> clusters,
        IReadOnlySet<int> freeClusterIds,
        MergeGeometry? geometry = null)
    {
        return EnumerateMergeWindows(tables, clusters, freeClusterIds, geometry)
            .Where(plan => plan.Capacity >= partySize)
            .OrderBy(plan => plan.Capacity)
            .ThenBy(plan => plan.ClusterIds.Count)
            .ThenBy(plan => plan.ClusterIds.Min())
            .FirstOrDefault();
    }

    // True only if exactly these clusters form one possible merge right now.
    public static bool IsValidMerge(
        IReadOnlyCollection<int> clusterIds,
        IReadOnlyList<Table> tables,
        IReadOnlyList<TableCluster> clusters,
        IReadOnlySet<int> freeClusterIds,
        MergeGeometry? geometry = null)
    {
        var wanted = clusterIds.ToHashSet();

        return wanted.Count == clusterIds.Count
            && EnumerateMergeWindows(tables, clusters, freeClusterIds, geometry)
                .Any(plan => plan.ClusterIds.Count == wanted.Count && wanted.SetEquals(plan.ClusterIds));
    }

    // Every run of two or more consecutive free clusters along a straight touching line.
    public static IEnumerable<MergePlan> EnumerateMergeWindows(
        IReadOnlyList<Table> tables,
        IReadOnlyList<TableCluster> clusters,
        IReadOnlySet<int> freeClusterIds,
        MergeGeometry? geometry = null)
    {
        var capacityOf = clusters.ToDictionary(c => c.ClusterId, c => c.Capacity);

        foreach (var line in Lines(tables, geometry))
        {
            var segments = ClusterSegments(line);

            // A cluster appearing in two separate places of one line is not contiguous
            // there; treat it as unmergeable rather than guess.
            var broken = segments.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

            for (var start = 0; start < segments.Count; start++)
            {
                var ids = new List<int>();
                var total = 0;

                for (var end = start; end < segments.Count; end++)
                {
                    var clusterId = segments[end];
                    if (clusterId < 0
                        || broken.Contains(clusterId)
                        || !freeClusterIds.Contains(clusterId)
                        || !capacityOf.TryGetValue(clusterId, out var capacity))
                        break;

                    ids.Add(clusterId);
                    total += capacity;

                    if (ids.Count >= 2)
                        yield return new MergePlan(ids.ToList(), total);
                }
            }
        }
    }

    // Maximal runs of tables in a lane that can be pushed together (a lone table is a run of
    // one): consecutive tables whose gap is within the geometry's limit and free. Tables that
    // cannot merge are runs of one on their own.
    public static IReadOnlyList<IReadOnlyList<Table>> Lines(IReadOnlyList<Table> tables, MergeGeometry? geometry = null)
    {
        geometry ??= MergeGeometry.TouchingOnly;
        var lines = new List<IReadOnlyList<Table>>();

        foreach (var table in tables.Where(t => AxisOf(t) is null))
            lines.Add(new[] { table });

        foreach (var axis in new[] { MergeAxis.Horizontal, MergeAxis.Vertical })
        {
            var lanes = tables.Where(t => AxisOf(t) == axis).GroupBy(t => Lane(t, axis));

            foreach (var lane in lanes)
            {
                var ordered = lane.OrderBy(t => Along(t, axis)).ToList();
                var run = new List<Table> { ordered[0] };

                for (var i = 1; i < ordered.Count; i++)
                {
                    if (CanPushTogether(ordered[i - 1], ordered[i], axis, geometry))
                    {
                        run.Add(ordered[i]);
                    }
                    else
                    {
                        lines.Add(run);
                        run = new List<Table> { ordered[i] };
                    }
                }

                lines.Add(run);
            }
        }

        return lines;
    }

    // The clusters met along a line, in order. -1 = a table with no cluster, which breaks the run.
    private static List<int> ClusterSegments(IReadOnlyList<Table> line)
    {
        var segments = new List<int>();
        foreach (var table in line)
        {
            var clusterId = table.ClusterId ?? -1;
            if (segments.Count == 0 || segments[^1] != clusterId || clusterId < 0)
                segments.Add(clusterId);
        }

        return segments;
    }

    // `previous` and `next` are consecutive in their lane: the empty cells between them
    // (none when they touch) must not exceed the limit and must all be free.
    private static bool CanPushTogether(Table previous, Table next, MergeAxis axis, MergeGeometry geometry)
    {
        var gapStart = Along(previous, axis) + Extent(previous, axis);
        var gap = Along(next, axis) - gapStart;

        if (gap < 0 || gap > geometry.MaxGapCells)
            return false;

        for (var along = gapStart; along < gapStart + gap; along++)
        {
            for (var across = 0; across < Thickness(previous, axis); across++)
            {
                var cell = axis == MergeAxis.Horizontal
                    ? new GridPosition(along, previous.Position.Y + across)
                    : new GridPosition(previous.Position.X + across, along);

                if (geometry.BlockedCells.Contains(cell))
                    return false;
            }
        }

        return true;
    }

    // How the clusters in `clusterIds` merge: who stays, who owns the result, who slides where.
    //   - 3 or more clusters in the line: the middle one stays (the lower middle for an even
    //     count) and the others slide in against it.
    //   - 2 clusters: the waiter with the fewest seated groups among the waiters OF THESE
    //     clusters wins (ties: lowest waiter id) and the cluster in his area stays; if both
    //     clusters are his, or neither has a waiter, the lowest table id stays.
    // Null if these clusters do not form one possible merge. Whether they are free is the
    // caller's check (IsValidMerge); this only plans the geometry.
    public static MergeMovePlan? PlanMerge(
        IReadOnlyCollection<int> clusterIds,
        IReadOnlyList<Table> tables,
        IReadOnlyList<TableCluster> clusters,
        IReadOnlyDictionary<int, int> seatedGroupsByWaiter,
        MergeGeometry? geometry = null)
    {
        var wanted = clusterIds.ToHashSet();
        if (wanted.Count < 2 || wanted.Count != clusterIds.Count)
            return null;

        var byId = clusters.ToDictionary(c => c.ClusterId);
        if (wanted.Any(id => !byId.ContainsKey(id)))
            return null;

        foreach (var line in Lines(tables, geometry))
        {
            if (AxisOf(line[0]) is not { } axis)
                continue;

            var segments = ClusterSegments(line);
            if (segments.Count(wanted.Contains) != wanted.Count)
                continue;

            var first = segments.FindIndex(wanted.Contains);
            var window = segments.GetRange(first, wanted.Count);
            if (!wanted.SetEquals(window))
                continue;

            return PlanWindow(window, line, axis, byId, seatedGroupsByWaiter);
        }

        return null;
    }

    private static MergeMovePlan PlanWindow(
        List<int> window,
        IReadOnlyList<Table> line,
        MergeAxis axis,
        IReadOnlyDictionary<int, TableCluster> byId,
        IReadOnlyDictionary<int, int> seatedGroupsByWaiter)
    {
        var waiterId = window
            .Select(id => byId[id].AssignedWaiterId)
            .OfType<int>()
            .Distinct()
            .OrderBy(w => seatedGroupsByWaiter.GetValueOrDefault(w))
            .ThenBy(w => w)
            .Cast<int?>()
            .FirstOrDefault();

        int stayerIndex;
        if (window.Count >= 3)
        {
            stayerIndex = (window.Count - 1) / 2;
        }
        else
        {
            var candidates = waiterId is null
                ? window
                : window.Where(id => byId[id].AssignedWaiterId == waiterId).ToList();

            stayerIndex = window.IndexOf(candidates.OrderBy(id => byId[id].TableIds.Min()).First());
        }

        var units = window.Select(id => line.Where(t => t.ClusterId == id).ToList()).ToList();
        var moves = new List<TableMove>();

        // Left of the stayer, nearest first: each unit closes the gap to the one on its right.
        var neighbourStart = UnitStart(units[stayerIndex], axis);
        for (var i = stayerIndex - 1; i >= 0; i--)
        {
            var shift = neighbourStart - UnitEnd(units[i], axis);
            moves.AddRange(Slide(units[i], axis, shift));
            neighbourStart = UnitStart(units[i], axis) + shift;
        }

        // Right of the stayer, nearest first: each unit closes the gap to the one on its left.
        var neighbourEnd = UnitEnd(units[stayerIndex], axis);
        for (var i = stayerIndex + 1; i < units.Count; i++)
        {
            var shift = neighbourEnd - UnitStart(units[i], axis);
            moves.AddRange(Slide(units[i], axis, shift));
            neighbourEnd = UnitEnd(units[i], axis) + shift;
        }

        return new MergeMovePlan(window[stayerIndex], waiterId, moves.OrderBy(m => m.TableId).ToList());
    }

    // Where each table of a merged cluster goes back to (its HomePosition), or null if any
    // home cell is taken by something other than these tables (`blockedByOthers`, see
    // MergeGeometry.BlockedCells) — "check all, then commit all": no partial return.
    public static IReadOnlyList<TableMove>? PlanReturn(
        IEnumerable<Table> clusterTables, IReadOnlySet<GridPosition> blockedByOthers)
    {
        var moves = new List<TableMove>();

        foreach (var table in clusterTables.OrderBy(t => t.TableId))
        {
            if (table.Position == table.HomePosition)
                continue;

            var homeCells = Footprint.Cells(table.HomePosition, table.Hitbox, table.OccupiedCells);
            if (homeCells.Any(blockedByOthers.Contains))
                return null;

            moves.Add(new TableMove(table.TableId, table.Position, table.HomePosition));
        }

        return moves;
    }

    private static IEnumerable<TableMove> Slide(IEnumerable<Table> unit, MergeAxis axis, int shift)
    {
        if (shift == 0)
            yield break;

        foreach (var table in unit)
        {
            var to = axis == MergeAxis.Horizontal
                ? new GridPosition(table.Position.X + shift, table.Position.Y)
                : new GridPosition(table.Position.X, table.Position.Y + shift);

            yield return new TableMove(table.TableId, table.Position, to);
        }
    }

    private static int UnitStart(IEnumerable<Table> unit, MergeAxis axis) => unit.Min(t => Along(t, axis));

    private static int UnitEnd(IEnumerable<Table> unit, MergeAxis axis) => unit.Max(t => Along(t, axis) + Extent(t, axis));

    // Tables only touch if they share a row (horizontal) / column (vertical) of the
    // same thickness.
    private static (int Position, int Thickness) Lane(Table table, MergeAxis axis) =>
        axis == MergeAxis.Horizontal
            ? (table.Position.Y, table.Hitbox.Height)
            : (table.Position.X, table.Hitbox.Width);

    private static int Along(Table table, MergeAxis axis) =>
        axis == MergeAxis.Horizontal ? table.Position.X : table.Position.Y;

    private static int Extent(Table table, MergeAxis axis) =>
        axis == MergeAxis.Horizontal ? table.Hitbox.Width : table.Hitbox.Height;

    private static int Thickness(Table table, MergeAxis axis) =>
        axis == MergeAxis.Horizontal ? table.Hitbox.Height : table.Hitbox.Width;
}
