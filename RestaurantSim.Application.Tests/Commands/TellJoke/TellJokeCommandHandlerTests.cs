using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.TellJoke;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.TellJoke;

public class TellJokeCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IRandomProvider _random = Substitute.For<IRandomProvider>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly TellJokeCommandHandler _handler;

    public TellJokeCommandHandlerTests()
    {
        _handler = new TellJokeCommandHandler(_repository, _random, _publisher);
    }

    [Fact]
    public async Task Handle_SuccessRoll_AppliesPositiveDelta_ToPresentMembers()
    {
        var (restaurant, cluster, customer) = BuildScenario(charisma: 0.8f, mood: 50f); // Mid tier
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _random.NextDouble().Returns(0.1, 0.0); // 0.1 < EffectiveCharisma(0.8) -> success; 0.0 -> min of delta range

        await _handler.Handle(new TellJokeCommand(restaurant.RestaurantId, cluster.ClusterId, TargetCustomerId: null), CancellationToken.None);

        customer.Mood.Should().BeGreaterThan(50f); // Mid success range: +5 to +12
        cluster.RecentJokeCount.Should().Be(1);
        await _publisher.Received(1).Publish(Arg.Is<JokeTold>(e => e.WasSuccessful), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FailureRoll_AppliesNegativeDelta()
    {
        var (restaurant, cluster, customer) = BuildScenario(charisma: 0.2f, mood: 50f);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _random.NextDouble().Returns(0.9, 0.0); // 0.9 >= EffectiveCharisma(0.2) -> failure

        await _handler.Handle(new TellJokeCommand(restaurant.RestaurantId, cluster.ClusterId, TargetCustomerId: null), CancellationToken.None);

        customer.Mood.Should().BeLessThan(50f); // Mid failure range: -3 to -9
    }

    [Fact]
    public async Task Handle_AppliesOnePointFiveMultiplier_ToPersonTargetedMember_Only()
    {
        var (restaurant, cluster, customer) = BuildScenario(charisma: 0.8f, mood: 50f);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _random.NextDouble().Returns(0.1, 1.0); // success, max of delta range (12 for Mid success)

        await _handler.Handle(new TellJokeCommand(restaurant.RestaurantId, cluster.ClusterId, TargetCustomerId: customer.CustomerId), CancellationToken.None);

        // Mid success max delta = 12, x1.5 targeted multiplier = 18
        customer.Mood.Should().Be(50f + 18f);
    }

    [Fact]
    public async Task Handle_ReducesEffectiveCharisma_AsRecentJokeCountIncreases()
    {
        // JokeFatigueMultiplier(4) = max(0.2, 1 - 0.25*4) = max(0.2, 0) = 0.2
        var (restaurant, cluster, _) = BuildScenario(charisma: 1.0f, mood: 50f);
        cluster.RecentJokeCount = 4;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _random.NextDouble().Returns(0.19, 0.0); // 0.19 < EffectiveCharisma(1.0*0.2=0.2) -> still succeeds, barely

        var result = await _handler.Handle(new TellJokeCommand(restaurant.RestaurantId, cluster.ClusterId, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_IncrementsRecentJokeCount_AndUpdatesLastJokeTick()
    {
        var (restaurant, cluster, _) = BuildScenario(charisma: 0.8f, mood: 50f);
        restaurant.SimulationTick = 500;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _random.NextDouble().Returns(0.1, 0.0);

        await _handler.Handle(new TellJokeCommand(restaurant.RestaurantId, cluster.ClusterId, null), CancellationToken.None);

        cluster.RecentJokeCount.Should().Be(1);
        cluster.LastJokeTick.Should().Be(500);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenClusterIsNotOccupied()
    {
        var (restaurant, cluster, _) = BuildScenario(charisma: 0.8f, mood: 50f);
        cluster.OccupiedBy = null;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new TellJokeCommand(restaurant.RestaurantId, cluster.ClusterId, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new TellJokeCommand(Guid.NewGuid(), 1, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static (Restaurant Restaurant, TableCluster Cluster, Customer Customer) BuildScenario(float charisma, float mood)
    {
        var customer = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = mood, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Male, IsPresentAtTable = true
        };

        var group = new CustomerGroup
        {
            GroupId = 1, PartySize = 1, Members = new List<Customer> { customer }, ArrivalTick = 0,
            ClusterId = 10, LifecycleState = GroupLifecycleState.Seated
        };

        var cluster = new TableCluster { ClusterId = 10, TableIds = new List<int>(), Capacity = 2, OccupiedBy = 1 };

        var stats = new StaffStats();
        stats.Set(StatType.Charisma, charisma);
        var waiter = new StaffMember { StaffId = 1, Name = "Waiter A", Role = StaffRole.Waiter, State = StaffState.Idle, Stats = stats, AssignedMealPeriods = new[] { "lunch" } };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };
        restaurant.Floor.Clusters.Add(cluster);
        restaurant.StaffRoster.Staff.Add(waiter);
        restaurant.CustomerService.ActiveGroups.Add(group);

        return (restaurant, cluster, customer);
    }
}
