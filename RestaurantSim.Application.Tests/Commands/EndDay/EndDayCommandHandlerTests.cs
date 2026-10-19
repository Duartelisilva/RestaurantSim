using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.EndDay;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Statistics;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.EndDay;

public class EndDayCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly ISnapshotStore _snapshotStore = Substitute.For<ISnapshotStore>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly EndDayCommandHandler _handler;

    public EndDayCommandHandlerTests()
    {
        _handler = new EndDayCommandHandler(_repository, _presetRepository, _snapshotStore, _publisher);
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenActiveGroupsPresent()
    {
        var restaurant = BuildRestaurant(simulationTick: 1440);
        restaurant.CustomerService.ActiveGroups.Add(new CustomerGroup { GroupId = 1, PartySize = 1, Members = new List<Customer>(), ArrivalTick = 0 });
        SetupConfig(restaurant);

        var result = await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CurrentDayNumber.Should().Be(0);
        await _snapshotStore.DidNotReceive().AppendAsync(Arg.Any<Guid>(), Arg.Any<StatisticsSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenNotYetPastClosing()
    {
        var restaurant = BuildRestaurant(simulationTick: 800);
        SetupConfig(restaurant);

        var result = await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CurrentDayNumber.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AppendsRealSnapshot_WithAccumulatedStats()
    {
        var restaurant = BuildRestaurant(simulationTick: 1440);
        restaurant.DailyStats.Revenue = 500m;
        restaurant.DailyStats.CustomersServed = 20;
        restaurant.DailyStats.WeightedFinalMoodSum = 20 * 65f; // avg 65
        restaurant.DailyStats.TableOccupancySampleSum = 3.5f;
        restaurant.DailyStats.TableOccupancySampleCount = 5; // avg 0.7
        restaurant.DailyStats.ReputationAtDayStart = 20f;
        restaurant.Reputation = 23f; // +3 delta today
        SetupConfig(restaurant);

        StatisticsSnapshot? capturedSnapshot = null;
        await _snapshotStore.AppendAsync(Arg.Any<Guid>(), Arg.Do<StatisticsSnapshot>(s => capturedSnapshot = s), Arg.Any<CancellationToken>());

        await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        capturedSnapshot.Should().NotBeNull();
        capturedSnapshot!.Value.Revenue.Should().Be(500m);
        capturedSnapshot.Value.CustomersServed.Should().Be(20);
        capturedSnapshot.Value.AvgFinalMood.Should().Be(65f);
        capturedSnapshot.Value.TableUtilization.Should().Be(0.7f);
        capturedSnapshot.Value.ReputationDelta.Should().Be(3f);
        capturedSnapshot.Value.TotalGameplayDays.Should().Be(1);
        capturedSnapshot.Value.AvgWaitTicks.Should().Be(0f); // honestly unbuilt, not faked
        capturedSnapshot.Value.Costs.Should().Be(0m); // honestly unbuilt, not faked
    }

    [Fact]
    public async Task Handle_ResetsAccumulator_ForNextDay()
    {
        var restaurant = BuildRestaurant(simulationTick: 1440);
        restaurant.DailyStats.Revenue = 500m;
        restaurant.DailyStats.CustomersServed = 20;
        restaurant.Reputation = 23f;
        SetupConfig(restaurant);

        await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        restaurant.DailyStats.Revenue.Should().Be(0m);
        restaurant.DailyStats.CustomersServed.Should().Be(0);
        restaurant.DailyStats.ReputationAtDayStart.Should().Be(23f); // carries forward for tomorrow's delta
    }

    [Fact]
    public async Task Handle_EndsDay_AndAdvancesTickToNextOpening()
    {
        var restaurant = BuildRestaurant(simulationTick: 1440);
        var bathroom = new Bathroom { BathroomId = 1, Gender = BathroomGender.Male, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1, CleanlinessScore = 40f };
        restaurant.Floor.Bathrooms.Add(bathroom);
        SetupConfig(restaurant);

        var result = await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CurrentDayNumber.Should().Be(1);
        bathroom.CleanlinessScore.Should().Be(100f);
        restaurant.SimulationTick.Should().Be(3600);

        await _publisher.Received(1).Publish(Arg.Is<DayEnded>(e => e.NewDayNumber == 1 && e.Tick == 3600), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new EndDayCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private void SetupConfig(Restaurant restaurant)
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
            MenuItemIds = Array.Empty<int>(), ReputationStart = 20f,
            QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f,
            ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<MealPeriodDefinition>
            {
                new() { MealPeriodId = "lunch", StartTime = new TimeOfDay(12, 0), EndTime = new TimeOfDay(15, 0) },
                new() { MealPeriodId = "dinner", StartTime = new TimeOfDay(19, 0), EndTime = new TimeOfDay(0, 0) }
            },
            EventModuleIds = Array.Empty<string>(), TipBaseRate = 50f, TipMoodExponent = 0.75f,
            MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
    }

    private static Restaurant BuildRestaurant(long simulationTick) => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = simulationTick,
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
