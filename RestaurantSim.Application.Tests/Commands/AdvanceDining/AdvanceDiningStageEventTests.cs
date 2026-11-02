using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceDining;
using RestaurantSim.Application.Commands.DeliverOrderItem;
using RestaurantSim.Application.Commands.LeaveTable;
using RestaurantSim.Application.Commands.PayBill;
using RestaurantSim.Application.Commands.PlaceOrder;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceDining;

// GroupDiningStageChanged: raised once per group per tick, only when the group's DiningStage
// actually changed, after the restaurant was saved. The stage rules themselves are tested in
// Domain.Tests (DiningStageCalculatorTests); the nested commands are faked here, so each test
// makes the fake do what the real handler would do to the state.
public class AdvanceDiningStageEventTests
{
    private const long Now = 750; // 12:30 at 1 game minute per tick — inside "lunch"

    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IMenuRepository _menuRepository = Substitute.For<IMenuRepository>();
    private readonly IRandomProvider _random = Substitute.For<IRandomProvider>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IRequestHandler<PlaceOrderCommand, Result> _placeOrder = Substitute.For<IRequestHandler<PlaceOrderCommand, Result>>();
    private readonly IRequestHandler<DeliverOrderItemCommand, Result> _deliver = Substitute.For<IRequestHandler<DeliverOrderItemCommand, Result>>();
    private readonly IRequestHandler<PayBillCommand, Result> _payBill = Substitute.For<IRequestHandler<PayBillCommand, Result>>();
    private readonly IRequestHandler<LeaveTableCommand, Result> _leaveTable = Substitute.For<IRequestHandler<LeaveTableCommand, Result>>();
    private readonly AdvanceDiningCommandHandler _handler;

    private static readonly MenuItem Water = new()
    {
        MenuItemId = 1, Name = "Water", Category = MenuItemCategory.Adult, Course = ItemPhase.Drinks,
        RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 1.5m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    public AdvanceDiningStageEventTests()
    {
        _handler = new AdvanceDiningCommandHandler(
            _repository, _presetRepository, _menuRepository, _random, _publisher, _placeOrder, _deliver, _payBill, _leaveTable);

        _menuRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<MenuItem> { Water });
        _random.NextDouble().Returns(0.0);

        _placeOrder.Handle(Arg.Any<PlaceOrderCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _deliver.Handle(Arg.Any<DeliverOrderItemCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _payBill.Handle(Arg.Any<PayBillCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _leaveTable.Handle(Arg.Any<LeaveTableCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task StageUnchanged_PublishesNothingAsync()
    {
        var (restaurant, _) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, Now + 5);

        await RunAsync(restaurant);

        await _publisher.DidNotReceive().Publish(Arg.Any<GroupDiningStageChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderTaken_OrderingBecomesWaitingForFoodAsync()
    {
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, Now);
        _placeOrder.Handle(Arg.Any<PlaceOrderCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var order = new Order
            {
                OrderId = 1, GroupId = group.GroupId, ClusterId = 10, CreatedAtTick = Now,
                Items = new List<OrderItem> { Item(1, ItemPhase.Drinks, OrderItemStatus.Queued) }
            };
            restaurant.KitchenOps.ActiveOrders.Add(order);
            group.CurrentOrderId = order.OrderId;
            return Result.Success();
        });

        await RunAsync(restaurant);

        await _publisher.Received(1).Publish(
            Arg.Is<GroupDiningStageChanged>(e =>
                e.GroupId == 1 && e.Stage == DiningStage.WaitingForFood && e.NextVisit == DiningVisit.Main
                && e.Tick == Now && e.RestaurantId == restaurant.RestaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EverythingServed_WaitingForFoodBecomesEatingAsync()
    {
        var (restaurant, _) = Scenario(
            DiningVisit.Dessert, GroupPhaseWaitState.Serving, 0,
            Item(1, ItemPhase.EntryMain, OrderItemStatus.Ready), Item(2, ItemPhase.EntryMain, OrderItemStatus.Ready));
        _deliver.Handle(Arg.Any<DeliverOrderItemCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.Arg<DeliverOrderItemCommand>().OrderItemId;
            restaurant.KitchenOps.ActiveOrders[0].Items.First(i => i.OrderItemId == id).Status = OrderItemStatus.Served;
            return Result.Success();
        });

        await RunAsync(restaurant);

        await _publisher.Received(1).Publish(
            Arg.Is<GroupDiningStageChanged>(e => e.Stage == DiningStage.Eating && e.NextVisit == DiningVisit.Dessert),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FinishedEating_EatingBecomesOrderingAgainAsync()
    {
        var (restaurant, _) = Scenario(
            DiningVisit.Dessert, GroupPhaseWaitState.Serving, 0,
            Item(1, ItemPhase.EntryMain, OrderItemStatus.Served, consumedBy: Now - 1));

        await RunAsync(restaurant);

        await _publisher.Received(1).Publish(
            Arg.Is<GroupDiningStageChanged>(e => e.Stage == DiningStage.Ordering && e.NextVisit == DiningVisit.Dessert),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BillPaid_PayingBecomesLeavingAsync()
    {
        var (restaurant, group) = Scenario(
            DiningVisit.Bill, GroupPhaseWaitState.AutoVisitPending, Now,
            Item(1, ItemPhase.EntryMain, OrderItemStatus.Served, consumedBy: Now - 1));
        _payBill.Handle(Arg.Any<PayBillCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            group.CoursePhase = CoursePhase.Done;
            group.LifecycleState = GroupLifecycleState.Leaving;
            return Result.Success();
        });

        await RunAsync(restaurant);

        await _publisher.Received(1).Publish(
            Arg.Is<GroupDiningStageChanged>(e => e.Stage == DiningStage.Leaving), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GroupThatLeftTheRestaurant_PublishesNoStageEventAsync()
    {
        var (restaurant, group) = Scenario(
            DiningVisit.Bill, GroupPhaseWaitState.Serving, Now,
            Item(1, ItemPhase.Dessert, OrderItemStatus.Served, consumedBy: Now - 1));
        group.LifecycleState = GroupLifecycleState.Leaving;
        _leaveTable.Handle(Arg.Any<LeaveTableCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            restaurant.CustomerService.ActiveGroups.Remove(group);
            return Result.Success();
        });

        await RunAsync(restaurant);

        await _leaveTable.Received(1).Handle(Arg.Any<LeaveTableCommand>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<GroupDiningStageChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EventIsPublishedAfterTheRestaurantIsSavedAsync()
    {
        var calls = new List<string>();
        var (restaurant, _) = Scenario(
            DiningVisit.Dessert, GroupPhaseWaitState.Serving, 0,
            Item(1, ItemPhase.EntryMain, OrderItemStatus.Served, consumedBy: Now - 1));
        _repository.SaveAsync(Arg.Any<Restaurant>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("save");
            return Task.CompletedTask;
        });
        _publisher.Publish(Arg.Any<GroupDiningStageChanged>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("publish");
            return Task.CompletedTask;
        });

        await RunAsync(restaurant);

        calls.Should().Equal("save", "publish");
    }

    // ---------- helpers ----------

    private async Task<Result> RunAsync(Restaurant restaurant)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        return await _handler.Handle(new AdvanceDiningCommand(restaurant.RestaurantId), CancellationToken.None);
    }

    private (Restaurant Restaurant, CustomerGroup Group) Scenario(
        DiningVisit nextVisit, GroupPhaseWaitState wait, long nextTick, params OrderItem[] items)
    {
        var group = new CustomerGroup
        {
            GroupId = 1, PartySize = 2, ArrivalTick = 0, ClusterId = 10,
            Members = new List<Customer> { Person(1), Person(2) },
            LifecycleState = GroupLifecycleState.Seated, PhaseWaitState = wait,
            NextVisit = nextVisit, NextAutoVisitTick = nextTick
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = Now,
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };

        restaurant.StaffRoster.Staff.Add(new StaffMember
        {
            StaffId = 1, Name = "Waiter A", Role = StaffRole.Waiter, State = StaffState.Idle,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        });
        restaurant.CustomerService.ActiveGroups.Add(group);

        if (items.Length > 0)
        {
            var order = new Order { OrderId = 1, GroupId = 1, ClusterId = 10, Items = items.ToList(), CreatedAtTick = 0 };
            restaurant.KitchenOps.ActiveOrders.Add(order);
            group.CurrentOrderId = order.OrderId;
        }

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());
        return (restaurant, group);
    }

    private static OrderItem Item(int id, ItemPhase phase, OrderItemStatus status, long? consumedBy = null) => new()
    {
        OrderItemId = id, OwnerId = 1, MenuItemId = 1, Phase = phase, PriceAtOrder = 1m,
        Status = status, CreatedAtTick = 0, ConsumedByTick = consumedBy
    };

    private static Customer Person(int id) => new()
    {
        CustomerId = id, IsAdult = true, Mood = 60, PatienceLevel = 0.5f, Instability = 0.5f,
        SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
    };

    private static RestaurantConfig BuildConfig() => new()
    {
        ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
        TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
        MenuItemIds = new[] { 1 }, ReputationStart = 20f,
        QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f,
        ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
        ActiveMealPeriods = new List<MealPeriodDefinition>
        {
            new() { MealPeriodId = "lunch", StartTime = new TimeOfDay(12, 0), EndTime = new TimeOfDay(15, 0) }
        },
        EventModuleIds = Array.Empty<string>(), TipBaseRate = 50f, TipMoodExponent = 0.75f,
        MaxQueueLength = 12, BaseArrivalIntervalSeconds = 15f
    };
}
