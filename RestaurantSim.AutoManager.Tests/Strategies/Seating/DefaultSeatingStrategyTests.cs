using FluentAssertions;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.Domain;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.AutoManager.Tests.Strategies.Seating;

public class DefaultSeatingStrategyTests
{
    private readonly DefaultSeatingStrategy _strategy = new();

    [Fact]
    public void DecideNextSeating_SeatsFirstQueuedGroup_WhenAFittingClusterExists()
    {
        var restaurant = BuildRestaurant();
        var group = AddQueuedGroup(restaurant, groupId: 1, partySize: 2);
        var cluster = AddFreeCluster(restaurant, clusterId: 10, capacity: 2);

        var decision = _strategy.DecideNextSeating(restaurant);

        decision.Should().Be((group.GroupId, cluster.ClusterId));
    }

    [Fact]
    public void DecideNextSeating_SkipsLargerGroup_ToSeatSmallerGroupFurtherBack()
    {
        // Matches business logic §1.2/§1.3's own example: a 4-person family is
        // skipped in favor of a 2-person couple further back in the queue if
        // only a 2-top is free — greedy best-fit, not strict FIFO.
        var restaurant = BuildRestaurant();
        var family = AddQueuedGroup(restaurant, groupId: 1, partySize: 4);
        var couple = AddQueuedGroup(restaurant, groupId: 2, partySize: 2);
        var twoTop = AddFreeCluster(restaurant, clusterId: 10, capacity: 2);

        var decision = _strategy.DecideNextSeating(restaurant);

        decision.Should().Be((couple.GroupId, twoTop.ClusterId));
    }

    [Fact]
    public void DecideNextSeating_PrefersSmallestFittingCluster_OverALargerOne()
    {
        var restaurant = BuildRestaurant();
        var group = AddQueuedGroup(restaurant, groupId: 1, partySize: 2);
        var fourTop = AddFreeCluster(restaurant, clusterId: 10, capacity: 4);
        var twoTop = AddFreeCluster(restaurant, clusterId: 11, capacity: 2);

        var decision = _strategy.DecideNextSeating(restaurant);

        decision.Should().Be((group.GroupId, twoTop.ClusterId)); // preserves the 4-top for a larger group
    }

    [Fact]
    public void DecideNextSeating_ReturnsNull_WhenQueueIsEmpty()
    {
        var restaurant = BuildRestaurant();
        AddFreeCluster(restaurant, clusterId: 10, capacity: 2);

        var decision = _strategy.DecideNextSeating(restaurant);

        decision.Should().BeNull();
    }

    [Fact]
    public void DecideNextSeating_ReturnsNull_WhenNoClusterFitsAnyQueuedGroup()
    {
        var restaurant = BuildRestaurant();
        AddQueuedGroup(restaurant, groupId: 1, partySize: 6);
        AddFreeCluster(restaurant, clusterId: 10, capacity: 2); // too small

        var decision = _strategy.DecideNextSeating(restaurant);

        decision.Should().BeNull();
    }

    [Fact]
    public void DecideNextSeating_ReturnsNull_WhenOnlyOccupiedClustersExist()
    {
        var restaurant = BuildRestaurant();
        AddQueuedGroup(restaurant, groupId: 1, partySize: 2);
        var cluster = AddFreeCluster(restaurant, clusterId: 10, capacity: 2);
        cluster.OccupiedBy = 999; // already occupied

        var decision = _strategy.DecideNextSeating(restaurant);

        decision.Should().BeNull();
    }

    private static CustomerGroup AddQueuedGroup(Restaurant restaurant, int groupId, int partySize)
    {
        var group = new CustomerGroup
        {
            GroupId = groupId, PartySize = partySize, Members = new List<Customer>(), ArrivalTick = 0
        };
        restaurant.CustomerService.Queue.Add(group);
        return group;
    }

    private static TableCluster AddFreeCluster(Restaurant restaurant, int clusterId, int capacity)
    {
        var cluster = new TableCluster { ClusterId = clusterId, TableIds = new List<int>(), Capacity = capacity };
        restaurant.Floor.Clusters.Add(cluster);
        return cluster;
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
