using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.SkipToNextMealPeriod;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SkipToNextMealPeriod;

public class SkipToNextMealPeriodCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly SkipToNextMealPeriodCommandHandler _handler;

    public SkipToNextMealPeriodCommandHandlerTests()
    {
        _handler = new SkipToNextMealPeriodCommandHandler(_repository, _presetRepository);
    }

    [Fact]
    public async Task Handle_SkipsToNextPeriod_WhenInGapBetweenLunchAndDinner()
    {
        // 1000 minutes = 16:40, between lunch end (900=15:00) and dinner start (1140=19:00).
        var restaurant = BuildRestaurant(simulationTick: 1000);
        SetupConfig(restaurant);

        var result = await _handler.Handle(new SkipToNextMealPeriodCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.SimulationTick.Should().Be(1140); // jumped to dinner's 19:00 start, same day
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenNotInAGap()
    {
        var restaurant = BuildRestaurant(simulationTick: 800); // mid-lunch
        SetupConfig(restaurant);

        var result = await _handler.Handle(new SkipToNextMealPeriodCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        restaurant.SimulationTick.Should().Be(800);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenCustomersArePresent()
    {
        var restaurant = BuildRestaurant(simulationTick: 1000);
        restaurant.CustomerService.ActiveGroups.Add(new CustomerGroup { GroupId = 1, PartySize = 1, Members = new List<Customer>(), ArrivalTick = 0 });
        SetupConfig(restaurant);

        var result = await _handler.Handle(new SkipToNextMealPeriodCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenQueueIsNotEmpty()
    {
        var restaurant = BuildRestaurant(simulationTick: 1000);
        restaurant.CustomerService.Queue.Add(new CustomerGroup { GroupId = 1, PartySize = 1, Members = new List<Customer>(), ArrivalTick = 0 });
        SetupConfig(restaurant);

        var result = await _handler.Handle(new SkipToNextMealPeriodCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new SkipToNextMealPeriodCommand(Guid.NewGuid()), CancellationToken.None);

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
