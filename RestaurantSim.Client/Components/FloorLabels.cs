using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Components;

// Text the floor view draws on a table — kept out of the .razor file so it is
// plain, testable logic.
public static class FloorLabels
{
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
