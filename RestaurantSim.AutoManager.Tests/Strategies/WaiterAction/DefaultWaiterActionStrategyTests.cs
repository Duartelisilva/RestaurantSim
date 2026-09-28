using FluentAssertions;
using NSubstitute;
using RestaurantSim.AutoManager.Strategies.WaiterAction;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.AutoManager.Tests.Strategies.WaiterAction;

public class DefaultWaiterActionStrategyTests
{
    private readonly IRandomProvider _random = Substitute.For<IRandomProvider>();
    private DefaultWaiterActionStrategy Strategy => new(_random);

    [Fact]
    public void DecideNextJoke_ReturnsDecision_WhenRollSucceeds()
    {
        var (restaurant, cluster) = BuildScenario(mood: 10f); // Very Low tier -> 0.20 trigger chance
        _random.NextDouble().Returns(0.1); // < 0.20 -> triggers

        var decision = Strategy.DecideNextJoke(restaurant, autoJokeCooldownTicks: 60, moodGapThreshold: 20);

        decision.Should().Be((cluster.ClusterId, (int?)null));
    }

    [Fact]
    public void DecideNextJoke_ReturnsNull_WhenRollFails()
    {
        var (restaurant, _) = BuildScenario(mood: 10f);
        _random.NextDouble().Returns(0.9); // >= 0.20 -> doesn't trigger

        var decision = Strategy.DecideNextJoke(restaurant, autoJokeCooldownTicks: 60, moodGapThreshold: 20);

        decision.Should().BeNull();
    }

    [Fact]
    public void DecideNextJoke_ReturnsNull_WhenCooldownStillActive()
    {
        var (restaurant, cluster) = BuildScenario(mood: 10f);
        cluster.LastJokeTick = restaurant.SimulationTick - 10; // only 10 ticks since last joke
        _random.NextDouble().Returns(0.01); // would otherwise trigger

        var decision = Strategy.DecideNextJoke(restaurant, autoJokeCooldownTicks: 60, moodGapThreshold: 20);

        decision.Should().BeNull();
    }

    [Fact]
    public void DecideNextJoke_TargetsSpecificMember_WhenMoodGapExceedsThreshold()
    {
        var restaurant = BuildBaseRestaurant();
        var outlier = AddCustomer(restaurant, customerId: 1, mood: 10f);
        var others = AddCustomer(restaurant, customerId: 2, mood: 80f);
        var cluster = SeatGroup(restaurant, new List<Customer> { outlier, others });
        _random.NextDouble().Returns(0.01); // triggers (Very Low tier from outlier)

        var decision = Strategy.DecideNextJoke(restaurant, autoJokeCooldownTicks: 60, moodGapThreshold: 20);

        decision.Should().Be((cluster.ClusterId, (int?)outlier.CustomerId));
    }

    [Fact]
    public void DecideNextJoke_TargetsWholeTable_WhenNoClearOutlier()
    {
        var restaurant = BuildBaseRestaurant();
        var a = AddCustomer(restaurant, customerId: 1, mood: 45f);
        var b = AddCustomer(restaurant, customerId: 2, mood: 50f); // close moods, no outlier
        var cluster = SeatGroup(restaurant, new List<Customer> { a, b });
        _random.NextDouble().Returns(0.01);

        var decision = Strategy.DecideNextJoke(restaurant, autoJokeCooldownTicks: 60, moodGapThreshold: 20);

        decision.Should().Be((cluster.ClusterId, (int?)null));
    }

    private (Restaurant Restaurant, TableCluster Cluster) BuildScenario(float mood)
    {
        var restaurant = BuildBaseRestaurant();
        var customer = AddCustomer(restaurant, customerId: 1, mood: mood);
        var cluster = SeatGroup(restaurant, new List<Customer> { customer });
        return (restaurant, cluster);
    }

    private static Customer AddCustomer(Restaurant restaurant, int customerId, float mood) => new()
    {
        CustomerId = customerId, IsAdult = true, Mood = mood, PatienceLevel = 0.5f, Instability = 0.5f,
        SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
        Gender = Gender.Male, IsPresentAtTable = true
    };

    private static TableCluster SeatGroup(Restaurant restaurant, List<Customer> members)
    {
        var group = new CustomerGroup
        {
            GroupId = 1, PartySize = members.Count, Members = members, ArrivalTick = 0,
            ClusterId = 10, LifecycleState = GroupLifecycleState.Seated
        };
        var cluster = new TableCluster { ClusterId = 10, TableIds = new List<int>(), Capacity = 4, OccupiedBy = 1, LastJokeTick = 0 };

        restaurant.CustomerService.ActiveGroups.Add(group);
        restaurant.Floor.Clusters.Add(cluster);
        restaurant.SimulationTick = 1000;
        return cluster;
    }

    private static Restaurant BuildBaseRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
