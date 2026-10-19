using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.SpawnArrival;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SpawnArrival;

public class SpawnArrivalCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IRandomProvider _random = Substitute.For<IRandomProvider>();
    private readonly SpawnArrivalCommandHandler _handler;

    public SpawnArrivalCommandHandlerTests()
    {
        _random.NextDouble().Returns(0.5);
        _handler = new SpawnArrivalCommandHandler(_repository, _presetRepository, _publisher, new ArrivalGroupGenerator(_random));
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenNotYetDue()
    {
        var restaurant = BuildRestaurant(simulationTick: 100, nextArrivalTick: 200);
        var config = BuildConfig();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _handler.Handle(new SpawnArrivalCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SpawnsGroup_WhenDueAndWithinActivePeriod()
    {
        // 12:30 (750 min into day) falls inside lunch (12:00-15:00).
        var restaurant = BuildRestaurant(simulationTick: 750, nextArrivalTick: 750);
        var config = BuildConfig();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _handler.Handle(new SpawnArrivalCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.Queue.Should().HaveCount(1);
        restaurant.CustomerService.NextArrivalTick.Should().BeGreaterThan(restaurant.SimulationTick);
        var queued = restaurant.CustomerService.Queue.Single();
        await _publisher.Received(1).Publish(
            Arg.Is<RestaurantSim.Domain.DomainEvents.GroupArrived>(e =>
                e.GroupId == queued.GroupId && e.PartySize == queued.PartySize),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNotSpawn_WhenOutsideActivePeriod()
    {
        // 03:00 (180 min into day) is outside both lunch and dinner.
        var restaurant = BuildRestaurant(simulationTick: 180, nextArrivalTick: 180);
        var config = BuildConfig();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _handler.Handle(new SpawnArrivalCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TurnsGroupAway_WhenQueueIsFull()
    {
        var restaurant = BuildRestaurant(simulationTick: 750, nextArrivalTick: 750);
        var config = BuildConfig(maxQueueLength: 1);
        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = 999, PartySize = 2, Members = Array.Empty<Customer>(), ArrivalTick = 0
        });
        var reputationBefore = restaurant.Reputation;

        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _handler.Handle(new SpawnArrivalCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.Queue.Should().HaveCount(1); // unchanged — new group turned away
        restaurant.Reputation.Should().BeLessThan(reputationBefore);
        await _publisher.Received(1).Publish(
            Arg.Is<RestaurantSim.Domain.DomainEvents.GroupTurnedAway>(e => e.ReputationAfter == restaurant.Reputation),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new SpawnArrivalCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static Restaurant BuildRestaurant(long simulationTick, long nextArrivalTick)
    {
        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            SimulationTick = simulationTick,
            Reputation = 50f,
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState { NextArrivalTick = nextArrivalTick },
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        return restaurant;
    }

    private static RestaurantConfig BuildConfig(int maxQueueLength = 12) => new()
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
        ActiveMealPeriods = new List<MealPeriodDefinition>
        {
            new() { MealPeriodId = "lunch", StartTime = new TimeOfDay(12, 0), EndTime = new TimeOfDay(15, 0) },
            new() { MealPeriodId = "dinner", StartTime = new TimeOfDay(19, 0), EndTime = new TimeOfDay(0, 0) }
        },
        EventModuleIds = Array.Empty<string>(),
        TipBaseRate = 50f,
        TipMoodExponent = 0.75f,
        MaxQueueLength = maxQueueLength,
        BaseArrivalIntervalSeconds = 120f
    };
}
