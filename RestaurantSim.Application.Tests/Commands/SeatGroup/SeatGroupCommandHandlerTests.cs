using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SeatGroup;

public class SeatGroupCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SeatGroupCommandHandler _handler;

    public SeatGroupCommandHandlerTests()
    {
        _presetRepository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(BuildConfig());
        _handler = new SeatGroupCommandHandler(_repository, _presetRepository, _publisher);
    }

    [Fact]
    public async Task Handle_SchedulesTheFirstWaiterVisitAsync()
    {
        var restaurant = BuildRestaurant(out var group, out var cluster);
        restaurant.SimulationTick = 100;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new SeatGroupCommand(restaurant.RestaurantId, group.GroupId, cluster.ClusterId), CancellationToken.None);

        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
        group.NextVisit.Should().Be(DiningVisit.Drinks);
        group.NextAutoVisitTick.Should().Be(103); // 3 s at 1 tick per second
    }

    private static RestaurantConfig BuildConfig() => new()
    {
        ConfigId = "default_diner",
        StartingCash = 5000m,
        GameMinutesPerRealSecond = 1,
        TicksPerRealSecond = 1,
        TableLayoutId = "default_diner_layout",
        StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
        MenuItemIds = Array.Empty<int>(),
        ReputationStart = 20f,
        QueueWaitMoodDecayPerSecond = -0.4f,
        CourseWaitMoodDecayPerSecond = -0.3f,
        NaturalMoodRecoveryPerSecond = 0.5f,
        ProbabilityCheckpointIntervalSeconds = 3f,
        PathReplanMaxWaitSeconds = 5f,
        MaxCallAttempts = 3,
        ActiveMealPeriods = new List<RestaurantSim.Domain.Scheduling.MealPeriodDefinition>(),
        EventModuleIds = Array.Empty<string>(),
        TipBaseRate = 50f,
        TipMoodExponent = 0.75f,
        MaxQueueLength = 12,
        BaseArrivalIntervalSeconds = 15f
    };

    [Fact]
    public async Task Handle_SeatsGroup_WhenClusterIsFreeAndFitsAsync()
    {
        var restaurant = BuildRestaurant(out var group, out var cluster);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new SeatGroupCommand(restaurant.RestaurantId, group.GroupId, cluster.ClusterId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        group.LifecycleState.Should().Be(GroupLifecycleState.Seated);
        group.ClusterId.Should().Be(cluster.ClusterId);
        cluster.OccupiedBy.Should().Be(group.GroupId);
        restaurant.CustomerService.Queue.Should().NotContain(group);
        restaurant.CustomerService.ActiveGroups.Should().Contain(group);

        await _repository.Received(1).SaveAsync(restaurant, Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(
            Arg.Is<RestaurantSim.Domain.DomainEvents.GroupSeated>(e =>
                e.GroupId == group.GroupId && e.ClusterId == cluster.ClusterId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(
            new SeatGroupCommand(Guid.NewGuid(), 1, 1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenGroupIsNotQueuedAsync()
    {
        var restaurant = BuildRestaurant(out _, out var cluster);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new SeatGroupCommand(restaurant.RestaurantId, GroupId: 999, cluster.ClusterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenClusterDoesNotFitGroupAsync()
    {
        var restaurant = BuildRestaurant(out var group, out var cluster, partySize: 6, clusterCapacity: 2);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new SeatGroupCommand(restaurant.RestaurantId, group.GroupId, cluster.ClusterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenClusterIsNotFreeAsync()
    {
        var restaurant = BuildRestaurant(out var group, out var cluster);
        cluster.OccupiedBy = 999;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new SeatGroupCommand(restaurant.RestaurantId, group.GroupId, cluster.ClusterId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static Restaurant BuildRestaurant(
        out CustomerGroup group,
        out TableCluster cluster,
        int partySize = 2,
        int clusterCapacity = 2)
    {
        group = new CustomerGroup
        {
            GroupId = 1,
            PartySize = partySize,
            Members = Array.Empty<Customer>(),
            ArrivalTick = 0
        };

        cluster = new TableCluster
        {
            ClusterId = 1,
            TableIds = new List<int> { 1 },
            Capacity = clusterCapacity,
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        restaurant.Floor.Clusters.Add(cluster);
        restaurant.CustomerService.Queue.Add(group);

        return restaurant;
    }
}
