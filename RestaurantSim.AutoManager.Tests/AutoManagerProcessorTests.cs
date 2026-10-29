using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestaurantSim.Application.Commands.CleanBathroom;
using RestaurantSim.Application.Commands.CleanTable;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Commands.TellJoke;
using RestaurantSim.Application.Common;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.AutoManager.Strategies.TableLayout;
using RestaurantSim.AutoManager.Strategies.TaskPriority;
using RestaurantSim.AutoManager.Strategies.WaiterAction;
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
    private readonly IWaiterActionStrategy _waiterActionStrategy = Substitute.For<IWaiterActionStrategy>();
    private readonly ILogger<AutoManagerProcessor> _logger = Substitute.For<ILogger<AutoManagerProcessor>>();
    private readonly AutoManagerProcessor _processor;
    private readonly ITableLayoutStrategy _tableLayoutStrategy = Substitute.For<ITableLayoutStrategy>();
    public AutoManagerProcessorTests()
    {
        _processor = new AutoManagerProcessor(_repository, _presetRepository, _sender, _seatingStrategy, _tableLayoutStrategy, _taskPriorityStrategy, _waiterActionStrategy, _logger);
        _tableLayoutStrategy.DecideNextLayoutChange(Arg.Any<Restaurant>()).Returns((TableLayoutDecision?)null);
        SetupConfig();
        _seatingStrategy.DecideNextSeating(Arg.Any<Restaurant>()).Returns((ValueTuple<int, int>?)null);
    }

    [Fact]
    public async Task RunPassAsync_DispatchesSeatGroupCommand_ForEachDecision_UntilStrategyReturnsNullAsync()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _sender.Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _seatingStrategy.DecideNextSeating(restaurant).Returns((1, 10),(2, 11),(ValueTuple<int, int>?)null);

        
        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(1).Send(Arg.Is<SeatGroupCommand>(c => c.GroupId == 1 && c.ClusterId == 10), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Is<SeatGroupCommand>(c => c.GroupId == 2 && c.ClusterId == 11), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_DispatchesTellJokeCommand_WhenWaiterActionStrategyDecidesAsync()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _waiterActionStrategy.DecideNextJoke(Arg.Any<Restaurant>(), Arg.Any<float>(), Arg.Any<float>()).Returns((10, (int?)5));
        _sender.Send(Arg.Any<TellJokeCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(1).Send(Arg.Is<TellJokeCommand>(c => c.ClusterId == 10 && c.TargetCustomerId == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_SkipsWaiterActionsEntirely_WhenToggleIsOffAsync()
    {
        var restaurant = BuildRestaurant();
        restaurant.AutoManagerToggles[AutoManagerSystem.WaiterActions] = false;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        _waiterActionStrategy.DidNotReceive().DecideNextJoke(Arg.Any<Restaurant>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task RunPassAsync_DoesNotDispatchTellJoke_WhenStrategyReturnsNullAsync()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _waiterActionStrategy.DecideNextJoke(Arg.Any<Restaurant>(), Arg.Any<float>(), Arg.Any<float>()).Returns((ValueTuple<int, int?>?)null);

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.DidNotReceive().Send(Arg.Any<TellJokeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_DoesNothing_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var act = async () => await _processor.RunPassAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private void SetupConfig()
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
            MenuItemIds = Array.Empty<int>(), ReputationStart = 20f,
            QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f,
            ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<RestaurantSim.Domain.Scheduling.MealPeriodDefinition>(),
            EventModuleIds = Array.Empty<string>(), TipBaseRate = 50f, TipMoodExponent = 0.75f,
            MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f
        };
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(config);
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
