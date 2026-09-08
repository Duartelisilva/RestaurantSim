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
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.EndDay;

public class EndDayCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly EndDayCommandHandler _handler;

    public EndDayCommandHandlerTests()
    {
        _handler = new EndDayCommandHandler(_repository, _presetRepository, _publisher);
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenActiveGroupsPresent()
    {
        var restaurant = BuildRestaurant(simulationTick: 1440); // exactly at closing wrap-point
        restaurant.CustomerService.ActiveGroups.Add(new CustomerGroup { GroupId = 1, PartySize = 1, Members = new List<Customer>(), ArrivalTick = 0 });
        SetupConfig(restaurant);

        var result = await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CurrentDayNumber.Should().Be(0); // unchanged
        restaurant.SimulationTick.Should().Be(1440); // unchanged
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenQueueNotEmpty()
    {
        var restaurant = BuildRestaurant(simulationTick: 1440);
        restaurant.CustomerService.Queue.Add(new CustomerGroup { GroupId = 1, PartySize = 1, Members = new List<Customer>(), ArrivalTick = 0 });
        SetupConfig(restaurant);

        var result = await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CurrentDayNumber.Should().Be(0);
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenNotYetPastClosing()
    {
        var restaurant = BuildRestaurant(simulationTick: 800); // 13:20, well within lunch
        SetupConfig(restaurant);

        var result = await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CurrentDayNumber.Should().Be(0);
        restaurant.SimulationTick.Should().Be(800);
    }

    [Fact]
    public async Task Handle_EndsDay_WhenPastClosingAndNoCustomersPresent()
    {
        // default_diner's dinner ends at 00:00 (Closing=1440) — the wrap-around
        // special case. minutesIntoDay==0 at tick 1440 (exactly one day elapsed).
        var restaurant = BuildRestaurant(simulationTick: 1440);
        var bathroom = new Bathroom { BathroomId = 1, Gender = BathroomGender.Male, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1, CleanlinessScore = 40f };
        restaurant.Floor.Bathrooms.Add(bathroom);
        SetupConfig(restaurant);

        var result = await _handler.Handle(new EndDayCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CurrentDayNumber.Should().Be(1);
        bathroom.CleanlinessScore.Should().Be(100f);
        // ticksPerDay=1440, currentDayStartTick=1440, Opening=720 (12:00) -> 1440+1440+720=3600
        restaurant.SimulationTick.Should().Be(3600);

        await _publisher.Received(1).Publish(
            Arg.Is<DayEnded>(e => e.NewDayNumber == 1 && e.Tick == 3600),
            Arg.Any<CancellationToken>());
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
            ConfigId = "default_diner", StartingCash = 5000m, MinutesPerTick = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
            MenuItemIds = Array.Empty<int>(), ReputationStart = 20f,
            QueueWaitMoodDecayRate = -0.4f, CourseWaitMoodDecayRate = -0.3f, NaturalMoodRecoveryRate = 0.5f,
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
