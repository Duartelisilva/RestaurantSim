using RestaurantSim.Domain;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.AutoManager.Strategies.TableLayout;

// What a real restaurant does with pushable tables:
//   1. A merged table group that is free again goes back to its normal state — unless a
//      waiting party needs it (too big for any single table of the group, but it fits
//      the merged one), in which case that party gets it.
//   2. If anyone waiting can sit at a free table as it is, seating comes first — and so
//      does waiting for a table that is already being pushed together (or put back) and
//      will fit them, instead of starting a second merge for the same party.
//   3. Otherwise the first waiting party that fits nowhere gets tables pushed together
//      (the smallest merge that is big enough).
// Which tables can merge at all is TableMergeRules' business, not this strategy's.
public sealed class DefaultTableLayoutStrategy : ITableLayoutStrategy
{
    public TableLayoutDecision? DecideNextLayoutChange(Restaurant restaurant)
    {
        var floor = restaurant.Floor;
        var queue = restaurant.CustomerService.Queue;
        var freeClusterIds = FloorQueries.FreeClusterIds(restaurant);
        var geometry = MergeGeometry.For(floor);

        var unneeded = floor.Clusters
            .Where(cluster => cluster.IsMergedOnDemand && freeClusterIds.Contains(cluster.ClusterId))
            .OrderBy(cluster => cluster.ClusterId)
            .FirstOrDefault(cluster => !queue.Any(group => NeedsMergedCluster(group, cluster)));

        if (unneeded is not null)
            return new SplitClusterDecision(unneeded.ClusterId);

        if (queue.Any(group => FitsAFreeCluster(restaurant, group) || FitsAMovingCluster(restaurant, group)))
            return null;

        foreach (var group in queue)
        {
            var plan = TableMergeRules.FindMerge(group.PartySize, floor.Tables, floor.Clusters, freeClusterIds, geometry);
            if (plan is not null)
                return new MergeClustersDecision(plan.ClusterIds);
        }

        return null;
    }

    private static bool NeedsMergedCluster(CustomerGroup group, TableCluster cluster)
    {
        return cluster.MergedFrom is { } parts
            && group.PartySize > parts.Max(part => part.Capacity)
            && group.PartySize <= cluster.Capacity;
    }

    // A cluster that is sliding into (or back from) a merge is not seatable yet but will be,
    // within a few ticks: its capacity counts as "on its way".
    private static bool FitsAMovingCluster(Restaurant restaurant, CustomerGroup group)
    {
        return restaurant.Floor.Clusters.Any(cluster =>
            cluster.Capacity >= group.PartySize
            && cluster.DeriveState(
                restaurant.Floor.Tables,
                restaurant.CustomerService.Reservations,
                restaurant.SimulationTick) == TableClusterState.Moving);
    }

    private static bool FitsAFreeCluster(Restaurant restaurant, CustomerGroup group)
    {
        return restaurant.Floor.Clusters.Any(cluster => cluster.CanSeat(
            group.PartySize,
            restaurant.Floor.Tables,
            restaurant.CustomerService.Reservations,
            restaurant.SimulationTick));
    }
}
