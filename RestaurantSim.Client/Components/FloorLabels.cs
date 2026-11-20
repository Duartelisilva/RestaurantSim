using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Components;

// Text the floor view draws on a table — kept out of the .razor file so it is
// plain, testable logic.
public static class FloorLabels
{
    // The group currently occupying this table's cluster (every table of a merged cluster
    // returns it), or null when the cluster is free or the client never saw the group seated.
    public static ActiveGroupDto? DiningGroup(RestaurantSnapshotDto snapshot, TableDto table)
    {
        var cluster = snapshot.Floor.Clusters.FirstOrDefault(c => c.ClusterId == table.ClusterId);
        if (cluster is not { OccupiedByGroupId: { } groupId })
            return null;

        return snapshot.ActiveGroups.FirstOrDefault(g => g.GroupId == groupId);
    }

    // The rectangle (in cells) around all the tables of a cluster — what the floor view outlines
    // when several tables have been pushed together. Null if the client knows none of them.
    public static (int X, int Y, int Width, int Height)? ClusterBounds(RestaurantSnapshotDto snapshot, ClusterDto cluster)
    {
        var tables = snapshot.Floor.Tables.Where(t => cluster.TableIds.Contains(t.TableId)).ToList();
        if (tables.Count == 0)
            return null;

        var left = tables.Min(t => t.Position.X);
        var top = tables.Min(t => t.Position.Y);
        var right = tables.Max(t => t.Position.X + t.Size.Width);
        var bottom = tables.Max(t => t.Position.Y + t.Size.Height);
        return (left, top, right - left, bottom - top);
    }

    // "G7 ×3" for group 7 (party of 3) seated at this table's cluster, shown on
    // the cluster's lowest-numbered table only so a merged cluster is labelled
    // once. A group the client never saw queue has no known party size and is
    // shown as "G7". Null when nothing should be drawn.
    public static string? Occupancy(RestaurantSnapshotDto snapshot, TableDto table)
    {
        var cluster = snapshot.Floor.Clusters.FirstOrDefault(c => c.ClusterId == table.ClusterId);
        if (cluster is not { OccupiedByGroupId: { } groupId })
            return null;

        if (cluster.TableIds.Min() != table.TableId)
            return null;

        var group = snapshot.ActiveGroups.FirstOrDefault(g => g.GroupId == groupId);
        return group is null ? $"G{groupId}" : $"G{groupId} ×{group.PartySize}";
    }
}
