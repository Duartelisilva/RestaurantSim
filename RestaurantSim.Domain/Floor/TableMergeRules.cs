namespace RestaurantSim.Domain.Floor;

public enum MergeAxis
{
    Horizontal, // tables are pushed together left-right (seats face North/South)
    Vertical    // tables are pushed together top-bottom (seats face East/West)
}

// A set of whole clusters that could be merged into one, and the capacity it would have.
public sealed record MergePlan(IReadOnlyList<int> ClusterIds, int Capacity);

// Pure geometry of merging (entity doc §2 CanMerge, §4 merge-axis rule, §5 no cap /
// single line only). Both the future MergeClustersCommand's validation and the
// AutoManager's seating strategy go through these methods, so "can these tables merge"
// has exactly one definition (CLAUDE.md: no duplicated rules).
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
    public static int MaxPartySize(IReadOnlyList<Table> tables)
    {
        var best = 0;
        foreach (var line in Lines(tables))
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
        IReadOnlySet<int> freeClusterIds)
    {
        return EnumerateMergeWindows(tables, clusters, freeClusterIds)
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
        IReadOnlySet<int> freeClusterIds)
    {
        var wanted = clusterIds.ToHashSet();

        return wanted.Count == clusterIds.Count
            && EnumerateMergeWindows(tables, clusters, freeClusterIds)
                .Any(plan => plan.ClusterIds.Count == wanted.Count && wanted.SetEquals(plan.ClusterIds));
    }

    // Every run of two or more consecutive free clusters along a straight touching line.
    public static IEnumerable<MergePlan> EnumerateMergeWindows(
        IReadOnlyList<Table> tables,
        IReadOnlyList<TableCluster> clusters,
        IReadOnlySet<int> freeClusterIds)
    {
        var capacityOf = clusters.ToDictionary(c => c.ClusterId, c => c.Capacity);

        foreach (var line in Lines(tables))
        {
            // The clusters met along the line, in order. -1 = a table with no cluster,
            // which breaks the run.
            var segments = new List<int>();
            foreach (var table in line)
            {
                var clusterId = table.ClusterId ?? -1;
                if (segments.Count == 0 || segments[^1] != clusterId || clusterId < 0)
                    segments.Add(clusterId);
            }

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

    // Maximal runs of touching tables in a lane (a lone table is a run of one). Tables
    // that cannot merge are runs of one on their own.
    public static IReadOnlyList<IReadOnlyList<Table>> Lines(IReadOnlyList<Table> tables)
    {
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
                    if (Along(ordered[i - 1], axis) + Extent(ordered[i - 1], axis) == Along(ordered[i], axis))
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
}
