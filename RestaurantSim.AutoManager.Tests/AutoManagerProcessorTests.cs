using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestaurantSim.Application.Commands.CleanBathroom;
using RestaurantSim.Application.Commands.CleanTable;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Common;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.AutoManager.Strategies.TaskPriority;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.AutoManager.Tests;

public class AutoManagerProcessorTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ISeatingStrategy _seatingStrategy = Substitute.For<ISeatingStrategy>();
    private readonly IStaffTaskPriorityStrategy _taskPriorityStrategy = Substitute.For<IStaffTaskPriorityStrategy>();
    private readonly ILogger<AutoManagerProcessor> _logger = Substitute.For<ILogger<AutoManagerProcessor>>();
    private readonly AutoManagerProcessor _processor;

    public AutoManagerProcessorTests()
    {
        _processor = new AutoManagerProcessor(_repository, _presetRepository, _sender, _seatingStrategy, _taskPriorityStrategy, _logger);
        SetupConfig();
    }

    [Fact]
    public async Task RunPassAsync_DispatchesSeatGroupCommand_ForEachDecision_UntilStrategyReturnsNull()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _sender.Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        var decisions = new Queue<(int GroupId, int ClusterId)?>(
        [
            (1, 10),
            (2, 11),
            null
        ]);

        _seatingStrategy
            .DecideNextSeating(restaurant)
            .Returns(_ => decisions.Dequeue());

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(1).Send(Arg.Is<SeatGroupCommand>(c => c.GroupId == 1 && c.ClusterId == 10), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Is<SeatGroupCommand>(c => c.GroupId == 2 && c.ClusterId == 11), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_SkipsSeatingEntirely_WhenSeatingToggleIsOff()
    {
        var restaurant = BuildRestaurant();
        restaurant.AutoManagerToggles[AutoManagerSystem.Seating] = false;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        _seatingStrategy.DidNotReceive().DecideNextSeating(Arg.Any<Restaurant>());
    }

    [Fact]
    public async Task RunPassAsync_DispatchesCleanTableCommand_ForIdleBusser_WhenStrategyPicksTable()
    {
        var restaurant = BuildRestaurant();
        var busser = AddIdleBusser(restaurant);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _seatingStrategy.DecideNextSeating(Arg.Any<Restaurant>()).Returns((ValueTuple<int, int>?)null);
        _taskPriorityStrategy.DecideNextBusserTask(Arg.Any<Restaurant>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns((TaskType.CleanTable, 5), (ValueTuple<TaskType, int>?)null);
        _sender.Send(Arg.Any<CleanTableCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(1).Send(Arg.Is<CleanTableCommand>(c => c.BusserStaffId == busser.StaffId && c.TableId == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_DispatchesBussersCleanBathroomCommand_WhenStrategyPicksBathroom()
    {
        var restaurant = BuildRestaurant();
        var busser = AddIdleBusser(restaurant);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _seatingStrategy.DecideNextSeating(Arg.Any<Restaurant>()).Returns((ValueTuple<int, int>?)null);
        _taskPriorityStrategy.DecideNextBusserTask(Arg.Any<Restaurant>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns((TaskType.CleanBathroom, 3), (ValueTuple<TaskType, int>?)null);
        _sender.Send(Arg.Any<BussersCleanBathroomCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(1).Send(Arg.Is<BussersCleanBathroomCommand>(c => c.BusserStaffId == busser.StaffId && c.BathroomId == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_SkipsTaskPriorityEntirely_WhenToggleIsOff()
    {
        var restaurant = BuildRestaurant();
        restaurant.AutoManagerToggles[AutoManagerSystem.TaskPriority] = false;
        AddIdleBusser(restaurant);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _seatingStrategy.DecideNextSeating(Arg.Any<Restaurant>()).Returns((ValueTuple<int, int>?)null);

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        _taskPriorityStrategy.DidNotReceive().DecideNextBusserTask(Arg.Any<Restaurant>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task RunPassAsync_DoesNothing_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var act = async () => await _processor.RunPassAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private void SetupConfig()
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
            MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f
        };
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(config);
    }

    private static StaffMember AddIdleBusser(Restaurant restaurant)
    {
        var busser = new StaffMember
        {
            StaffId = 1, Name = "Busser A", Role = StaffRole.Busser, State = StaffState.Idle,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        };
        restaurant.StaffRoster.Staff.Add(busser);
        return busser;
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
