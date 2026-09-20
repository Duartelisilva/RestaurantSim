using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceCleaningProgress;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceCleaningProgress;

public class AdvanceCleaningProgressCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly AdvanceCleaningProgressCommandHandler _handler;

    public AdvanceCleaningProgressCommandHandlerTests()
    {
        _handler = new AdvanceCleaningProgressCommandHandler(_repository, _presetRepository, _publisher);
    }

    [Fact]
    public async Task Handle_ProgressesTableCleaning_ForBusyBusser()
    {
        var (restaurant, busser, table) = BuildScenario(taskType: TaskType.CleanTable, targetId: 1, initialProgress: 0f);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceCleaningProgressCommand(restaurant.RestaurantId), CancellationToken.None);

        table.CleaningProgress.Should().BeGreaterThan(0f);
        table.IsClean.Should().BeFalse(); // not done yet
        busser.State.Should().Be(StaffState.Busy); // still working
    }

    [Fact]
    public async Task Handle_CompletesTableCleaning_AndFreesBusser_WhenProgressReaches100()
    {
        var (restaurant, busser, table) = BuildScenario(taskType: TaskType.CleanTable, targetId: 1, initialProgress: 99.9f);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceCleaningProgressCommand(restaurant.RestaurantId), CancellationToken.None);

        table.IsClean.Should().BeTrue();
        table.CleaningProgress.Should().Be(0f); // reset after completion
        busser.State.Should().Be(StaffState.Idle);
        busser.CurrentTaskType.Should().BeNull();

        await _publisher.Received(1).Publish(Arg.Any<TableCleaned>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CompletesBathroomCleaning_WhenCleanlinessReaches100()
    {
        var restaurant = BuildBaseRestaurant();
        var bathroom = new Bathroom
        {
            BathroomId = 1, Gender = BathroomGender.Male, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1),
            Capacity = 1, MaxConcurrentCleaners = 1, CleanlinessScore = 99.9f
        };
        restaurant.Floor.Bathrooms.Add(bathroom);
        var busser = AddBusyBusser(restaurant, TaskType.CleanBathroom, targetId: 1);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceCleaningProgressCommand(restaurant.RestaurantId), CancellationToken.None);

        bathroom.CleanlinessScore.Should().Be(100f);
        busser.State.Should().Be(StaffState.Idle);

        await _publisher.Received(1).Publish(Arg.Any<BathroomCleaned>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenNoBusyBussers()
    {
        var restaurant = BuildBaseRestaurant();
        SetupConfig(restaurant);

        var result = await _handler.Handle(new AdvanceCleaningProgressCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _publisher.DidNotReceive().Publish(Arg.Any<TableCleaned>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new AdvanceCleaningProgressCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private void SetupConfig(Restaurant restaurant)
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, MinutesPerTick = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
            MenuItemIds = Array.Empty<int>(), ReputationStart = 20f,
            QueueWaitMoodDecayRate = -0.4f, CourseWaitMoodDecayRate = -0.3f, NaturalMoodRecoveryRate = 0.5f,
            ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<RestaurantSim.Domain.Scheduling.MealPeriodDefinition>(),
            EventModuleIds = Array.Empty<string>(), TipBaseRate = 50f, TipMoodExponent = 0.75f,
            MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f,
            TableCleaningBaseSeconds = 10f // deterministic short test value
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
    }

    private static (Restaurant Restaurant, StaffMember Busser, Table Table) BuildScenario(TaskType taskType, int targetId, float initialProgress)
    {
        var restaurant = BuildBaseRestaurant();
        var table = new Table
        {
            TableId = 1, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1),
            Shape = new TableShape { ShapeId = "standard_2top", SeatDefinitions = Array.Empty<SeatDefinition>(), CanMerge = false },
            Seats = Array.Empty<Seat>(), IsClean = false, CleaningProgress = initialProgress
        };
        restaurant.Floor.Tables.Add(table);
        var busser = AddBusyBusser(restaurant, taskType, targetId);
        return (restaurant, busser, table);
    }

    private static StaffMember AddBusyBusser(Restaurant restaurant, TaskType taskType, int targetId)
    {
        var stats = new StaffStats();
        stats.Set(StatType.Speed, 0.5f);
        stats.Set(StatType.Fatigue, 0f);

        var busser = new StaffMember
        {
            StaffId = 1, Name = "Busser A", Role = StaffRole.Busser, State = StaffState.Busy,
            CurrentTaskType = taskType, CurrentTaskTargetId = targetId,
            Stats = stats, AssignedMealPeriods = new[] { "lunch" }
        };
        restaurant.StaffRoster.Staff.Add(busser);
        return busser;
    }

    private static Restaurant BuildBaseRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
