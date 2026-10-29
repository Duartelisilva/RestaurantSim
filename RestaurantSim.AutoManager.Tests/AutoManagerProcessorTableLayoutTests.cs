using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestaurantSim.Application.Commands.MergeClusters;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Commands.SplitCluster;
using RestaurantSim.Application.Common;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.AutoManager.Strategies.TableLayout;
using RestaurantSim.AutoManager.Strategies.TaskPriority;
using RestaurantSim.AutoManager.Strategies.WaiterAction;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.AutoManager.Tests;

public class AutoManagerProcessorTableLayoutTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ISeatingStrategy _seatingStrategy = Substitute.For<ISeatingStrategy>();
    private readonly ITableLayoutStrategy _tableLayoutStrategy = Substitute.For<ITableLayoutStrategy>();
    private readonly IStaffTaskPriorityStrategy _taskPriorityStrategy = Substitute.For<IStaffTaskPriorityStrategy>();
    private readonly IWaiterActionStrategy _waiterActionStrategy = Substitute.For<IWaiterActionStrategy>();
    private readonly ILogger<AutoManagerProcessor> _logger = Substitute.For<ILogger<AutoManagerProcessor>>();
    private readonly AutoManagerProcessor _processor;
    private readonly Restaurant _restaurant;

    public AutoManagerProcessorTableLayoutTests()
    {
        _processor = new AutoManagerProcessor(
            _repository, _presetRepository, _sender, _seatingStrategy, _tableLayoutStrategy,
            _taskPriorityStrategy, _waiterActionStrategy, _logger);

        _restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        _repository.GetAsync(_restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(_restaurant);
        _presetRepository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RestaurantConfig?)null);
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _seatingStrategy.DecideNextSeating(Arg.Any<Restaurant>()).Returns((ValueTuple<int, int>?)null);
        _tableLayoutStrategy.DecideNextLayoutChange(Arg.Any<Restaurant>()).Returns((TableLayoutDecision?)null);
    }

    [Fact]
    public async Task AMergeDecision_IsSentAsAMergeCommand_BeforeTheGroupIsSeatedAsync()
    {
        _tableLayoutStrategy.DecideNextLayoutChange(Arg.Any<Restaurant>())
            .Returns(
                (TableLayoutDecision?)new MergeClustersDecision(new[] { 1, 2 }),
                (TableLayoutDecision?)null);
        _seatingStrategy.DecideNextSeating(_restaurant).Returns((1, 1), (ValueTuple<int, int>?)null);

        await _processor.RunPassAsync(_restaurant.RestaurantId, CancellationToken.None);

        Received.InOrder(() =>
        {
            _sender.Send(Arg.Is<MergeClustersCommand>(c => c.ClusterIds.SequenceEqual(new[] { 1, 2 })), Arg.Any<CancellationToken>());
            _sender.Send(Arg.Is<SeatGroupCommand>(c => c.GroupId == 1 && c.ClusterId == 1), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ASplitDecision_IsSentAsASplitCommandAsync()
    {
        _tableLayoutStrategy.DecideNextLayoutChange(Arg.Any<Restaurant>())
            .Returns(
                (TableLayoutDecision?)new SplitClusterDecision(4),
                (TableLayoutDecision?)null);

        await _processor.RunPassAsync(_restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(1).Send(Arg.Is<SplitClusterCommand>(c => c.ClusterId == 4), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenALayoutCommandFails_TheSeatingPassStopsAsync()
    {
        _tableLayoutStrategy.DecideNextLayoutChange(_restaurant)
            .Returns((TableLayoutDecision?)new MergeClustersDecision(new[] { 1, 2 }));
        _sender.Send(Arg.Any<MergeClustersCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure("no longer free"));
        _seatingStrategy.DecideNextSeating(_restaurant).Returns((1, 1), (ValueTuple<int, int>?)null);

        await _processor.RunPassAsync(_restaurant.RestaurantId, CancellationToken.None);

        await _sender.DidNotReceive().Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithTheSeatingToggleOff_NoLayoutChangesAreMadeAsync()
    {
        _restaurant.AutoManagerToggles[AutoManagerSystem.Seating] = false;
        _tableLayoutStrategy.DecideNextLayoutChange(_restaurant)
            .Returns((TableLayoutDecision?)new MergeClustersDecision(new[] { 1, 2 }));

        await _processor.RunPassAsync(_restaurant.RestaurantId, CancellationToken.None);

        _tableLayoutStrategy.DidNotReceiveWithAnyArgs().DecideNextLayoutChange(default!);
        await _sender.DidNotReceive().Send(Arg.Any<MergeClustersCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStrategyThatNeverStops_IsCutOffByTheStepLimitAsync()
    {
        _tableLayoutStrategy.DecideNextLayoutChange(Arg.Any<Restaurant>())
            .Returns((TableLayoutDecision?)new SplitClusterDecision(1));

        await _processor.RunPassAsync(_restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(100).Send(Arg.Any<SplitClusterCommand>(), Arg.Any<CancellationToken>());
    }
}
