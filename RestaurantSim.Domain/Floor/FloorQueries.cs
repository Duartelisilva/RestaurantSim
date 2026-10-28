namespace RestaurantSim.Domain.Floor;

public static class FloorQueries
{
    // Ids of the clusters that are Free right now (clean, unoccupied, not reserved).
    public static IReadOnlySet<int> FreeClusterIds(Restaurant restaurant)
    {
        return restaurant.Floor.Clusters
            .Where(c => c.DeriveState(
                restaurant.Floor.Tables,
                restaurant.CustomerService.Reservations,
                restaurant.SimulationTick) == TableClusterState.Free)
            .Select(c => c.ClusterId)
            .ToHashSet();
    }
}
