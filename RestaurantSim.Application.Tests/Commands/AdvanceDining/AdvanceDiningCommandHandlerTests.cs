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
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceDining;

// The nested command handlers are faked and return success; these tests pin down the
// service-flow state machine itself (when it acts, what it orders, when it schedules).
public class AdvanceDiningCommandHandlerTests
{
    private const long Now = 750; // 12:30 at 1 game minute per tick — inside "lunch"

    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IMenuRepository _menuRepository = Substitute.For<IMenuRepository>();
    private readonly IRandomProvider _random = Substitute.For<IRandomProvider>();
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

    private static readonly MenuItem Steak = new()
    {
        MenuItemId = 5, Name = "Grilled Steak", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain,
        RequiredStationType = "grill", PrepTimeSeconds = 14, DefaultPrice = 12.5m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    private static readonly MenuItem Tart = new()
    {
        MenuItemId = 8, Name = "Custard Tart", Category = MenuItemCategory.Adult, Course = ItemPhase.Dessert,
        RequiredStationType = "dessert_station", PrepTimeSeconds = 3, DefaultPrice = 2m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    public AdvanceDiningCommandHandlerTests()
    {
        _handler = new AdvanceDiningCommandHandler(
            _repository, _presetRepository, _menuRepository, _random, _placeOrder, _deliver, _payBill, _leaveTable);

        _menuRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<MenuItem> { Water, Steak, Tart });
        _random.NextDouble().Returns(0.0);

        _placeOrder.Handle(Arg.Any<PlaceOrderCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _deliver.Handle(Arg.Any<DeliverOrderItemCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _payBill.Handle(Arg.Any<PayBillCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _leaveTable.Handle(Arg.Any<LeaveTableCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    // ---------- first visit / ordering ----------

    [Fact]
    public async Task VisitNotDueYet_DoesNothing()
    {
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, nextTick: Now + 1);

        await Run(restaurant);

        await _placeOrder.DidNotReceiveWithAnyArgs().Handle(default!, default);
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
    }

    [Fact]
    public async Task DrinksVisitDueAndReady_OrdersOneDrinkPerMember_AndMovesToServing()
    {
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, nextTick: Now);

        await Run(restaurant);

        await _placeOrder.Received(1).Handle(
            Arg.Is<PlaceOrderCommand>(c =>
                c.Phase == ItemPhase.Drinks && c.Items.Count == 2 && c.Items.All(i => i.MenuItemId == 1)),
            Arg.Any<CancellationToken>());
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.Serving);
        group.NextVisit.Should().Be(DiningVisit.Main);
    }

    [Fact]
    public async Task VisitDueButGroupNotReady_SchedulesARecheck()
    {
        _random.NextDouble().Returns(0.99); // above the 0.9 first-attempt chance
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, nextTick: Now);

        await Run(restaurant);

        await _placeOrder.DidNotReceiveWithAnyArgs().Handle(default!, default);
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.RecheckScheduled);
        group.VisitAttempt.Should().Be(1);
        group.NextAutoVisitTick.Should().Be(Now + 4);
    }

    [Fact]
    public async Task OnARecheck_TheSameRollThatFailedBeforeNowSucceeds()
    {
        _random.NextDouble().Returns(0.93); // fails at 0.90, passes at 0.95 (second attempt)
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.RecheckScheduled, nextTick: Now, attempt: 1);

        await Run(restaurant);

        await _placeOrder.Received(1).Handle(Arg.Any<PlaceOrderCommand>(), Arg.Any<CancellationToken>());
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.Serving);
        group.VisitAttempt.Should().Be(0);
    }

    [Fact]
    public async Task NoIdleWaiter_TheVisitStaysDue()
    {
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, waiterState: StaffState.Busy);

        await Run(restaurant);

        await _placeOrder.DidNotReceiveWithAnyArgs().Handle(default!, default);
        group.NextAutoVisitTick.Should().Be(Now);
    }

    [Fact]
    public async Task MainVisitDue_OrdersOneMainPerMember_ThenExpectsTheRefillVisit()
    {
        var (restaurant, group) = Scenario(DiningVisit.Main, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, course: CoursePhase.Drinks);

        await Run(restaurant);

        await _placeOrder.Received(1).Handle(
            Arg.Is<PlaceOrderCommand>(c => c.Phase == ItemPhase.EntryMain && c.Items.Count == 2 && c.Items.All(i => i.MenuItemId == 5)),
            Arg.Any<CancellationToken>());
        group.NextVisit.Should().Be(DiningVisit.Refill);
    }

    [Fact]
    public async Task PlaceOrderFails_TheFailureIsReported_AndTheVisitStaysDue()
    {
        _placeOrder.Handle(Arg.Any<PlaceOrderCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure("boom"));
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, nextTick: Now);

        var result = await Run(restaurant);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("boom");
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
    }

    // ---------- scheduling the next visit ----------

    [Fact]
    public async Task AllDrinksServed_SchedulesTheMainVisitFiveSecondsAhead()
    {
        var items = new[] { Item(1, 1, 1, ItemPhase.Drinks, OrderItemStatus.Served), Item(2, 2, 1, ItemPhase.Drinks, OrderItemStatus.Served) };
        var (restaurant, group) = Scenario(DiningVisit.Main, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.Drinks, items: items);

        await Run(restaurant);

        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
        group.NextAutoVisitTick.Should().Be(Now + 5);
    }

    [Fact]
    public async Task DrinksNotAllServed_NothingIsScheduledYet()
    {
        var items = new[] { Item(1, 1, 1, ItemPhase.Drinks, OrderItemStatus.Served), Item(2, 2, 1, ItemPhase.Drinks, OrderItemStatus.Queued) };
        var (restaurant, group) = Scenario(DiningVisit.Main, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.Drinks, items: items);

        await Run(restaurant);

        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.Serving);
    }

    [Fact]
    public async Task ScheduledDelays_AreInRealSeconds_SoTheyDoubleInTicksAtTwoTicksPerSecond()
    {
        var items = new[] { Item(1, 1, 1, ItemPhase.Drinks, OrderItemStatus.Served), Item(2, 2, 1, ItemPhase.Drinks, OrderItemStatus.Served) };
        var (restaurant, group) = Scenario(DiningVisit.Main, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.Drinks, items: items, ticksPerRealSecond: 2);

        await Run(restaurant);

        group.NextAutoVisitTick.Should().Be(Now + 10); // 5 s x 2 ticks per second
    }

    [Fact]
    public async Task AllMainsServed_SchedulesTheRefillVisit()
    {
        var items = new[] { Item(1, 1, 5, ItemPhase.EntryMain, OrderItemStatus.Served), Item(2, 2, 5, ItemPhase.EntryMain, OrderItemStatus.Served) };
        var (restaurant, group) = Scenario(DiningVisit.Refill, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.EntryMain, items: items);

        await Run(restaurant);

        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
        group.NextAutoVisitTick.Should().Be(Now + 4);
    }

    [Fact]
    public async Task MainsOnlySchedulesTheDessertVisitOnceEveryoneHasFinishedEating()
    {
        var eating = new[] { Item(1, 1, 5, ItemPhase.EntryMain, OrderItemStatus.Served, consumedBy: Now + 5), Item(2, 2, 5, ItemPhase.EntryMain, OrderItemStatus.Served, consumedBy: Now - 1) };
        var (restaurant, group) = Scenario(DiningVisit.Dessert, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.EntryMain, items: eating);

        await Run(restaurant);
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.Serving);

        eating[0].ConsumedByTick = Now - 1;
        await Run(restaurant);

        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
        group.NextAutoVisitTick.Should().Be(Now + 6);
    }

    [Fact]
    public async Task AllDessertsEaten_SchedulesTheBill()
    {
        var items = new[] { Item(1, 1, 8, ItemPhase.Dessert, OrderItemStatus.Served, consumedBy: Now - 1) };
        var (restaurant, group) = Scenario(DiningVisit.Bill, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.Dessert, items: items);

        await Run(restaurant);

        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
        group.NextAutoVisitTick.Should().Be(Now + 4);
    }

    // ---------- refill visit ----------

    [Fact]
    public async Task RefillVisit_OnlyMembersWhoFinishedTheirDrinkCanOrderAnother()
    {
        var items = new[]
        {
            Item(1, 1, 1, ItemPhase.Drinks, OrderItemStatus.Served, consumedBy: Now - 1),   // finished
            Item(2, 2, 1, ItemPhase.Drinks, OrderItemStatus.Served, consumedBy: Now + 10)   // still drinking
        };
        var (restaurant, group) = Scenario(DiningVisit.Refill, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, course: CoursePhase.EntryMain, items: items);

        await Run(restaurant);

        await _placeOrder.Received(1).Handle(
            Arg.Is<PlaceOrderCommand>(c => c.Phase == ItemPhase.Drinks && c.Items.Count == 1 && c.Items[0].CustomerId == 1),
            Arg.Any<CancellationToken>());
        group.NextVisit.Should().Be(DiningVisit.Dessert);
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.Serving);
    }

    [Fact]
    public async Task RefillVisit_NobodyAsksForAnother_StillMovesOn()
    {
        _random.NextDouble().Returns(0.9); // above every refill chance
        var items = new[] { Item(1, 1, 1, ItemPhase.Drinks, OrderItemStatus.Served, consumedBy: Now - 1) };
        var (restaurant, group) = Scenario(DiningVisit.Refill, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, course: CoursePhase.EntryMain, items: items);

        await Run(restaurant);

        await _placeOrder.DidNotReceiveWithAnyArgs().Handle(default!, default);
        group.NextVisit.Should().Be(DiningVisit.Dessert);
    }

    // ---------- dessert visit ----------

    [Fact]
    public async Task DessertVisit_WhenNobodyOrders_GoesStraightToTheBill()
    {
        _random.NextDouble().Returns(0.0, 0.9, 0.9); // ready; then both members decline
        var (restaurant, group) = Scenario(DiningVisit.Dessert, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, course: CoursePhase.EntryMain);

        await Run(restaurant);

        await _placeOrder.DidNotReceiveWithAnyArgs().Handle(default!, default);
        group.NextVisit.Should().Be(DiningVisit.Bill);
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.AutoVisitPending);
        group.NextAutoVisitTick.Should().Be(Now + 4);
    }

    [Fact]
    public async Task DessertVisit_WhenSomeoneOrders_PlacesTheDessertOrder()
    {
        var (restaurant, group) = Scenario(DiningVisit.Dessert, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, course: CoursePhase.EntryMain);

        await Run(restaurant);

        await _placeOrder.Received(1).Handle(
            Arg.Is<PlaceOrderCommand>(c => c.Phase == ItemPhase.Dessert && c.Items.Count == 2 && c.Items.All(i => i.MenuItemId == 8)),
            Arg.Any<CancellationToken>());
        group.NextVisit.Should().Be(DiningVisit.Bill);
        group.PhaseWaitState.Should().Be(GroupPhaseWaitState.Serving);
    }

    // ---------- bill, leaving ----------

    [Fact]
    public async Task BillVisitDue_PaysAndSchedulesTheLeave()
    {
        var (restaurant, group) = Scenario(DiningVisit.Bill, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, course: CoursePhase.Dessert);

        await Run(restaurant);

        await _payBill.Received(1).Handle(Arg.Is<PayBillCommand>(c => c.GroupId == group.GroupId), Arg.Any<CancellationToken>());
        group.NextAutoVisitTick.Should().Be(Now + 2);
    }

    [Fact]
    public async Task PayBillFails_IsReported_AndRetriedNextTick()
    {
        _payBill.Handle(Arg.Any<PayBillCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure("not eligible"));
        var (restaurant, group) = Scenario(DiningVisit.Bill, GroupPhaseWaitState.AutoVisitPending, nextTick: Now, course: CoursePhase.Dessert);

        var result = await Run(restaurant);

        result.IsFailure.Should().BeTrue();
        group.NextAutoVisitTick.Should().Be(Now + 1);
    }

    [Fact]
    public async Task LeavingGroup_LeavesOnceTheTimerIsDue_NotBefore()
    {
        var (restaurant, group) = Scenario(DiningVisit.Bill, GroupPhaseWaitState.Serving, nextTick: Now + 1, course: CoursePhase.Done);
        group.LifecycleState = GroupLifecycleState.Leaving;

        await Run(restaurant);
        await _leaveTable.DidNotReceiveWithAnyArgs().Handle(default!, default);

        group.NextAutoVisitTick = Now;
        await Run(restaurant);
        await _leaveTable.Received(1).Handle(Arg.Is<LeaveTableCommand>(c => c.GroupId == group.GroupId), Arg.Any<CancellationToken>());
    }

    // ---------- delivery ----------

    [Fact]
    public async Task ReadyStationItem_IsDelivered_AndGetsAnEatingTimer()
    {
        var steak = Item(1, 1, 5, ItemPhase.EntryMain, OrderItemStatus.Ready);
        var (restaurant, _) = Scenario(DiningVisit.Refill, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.EntryMain, items: new[] { steak });

        await Run(restaurant);

        await _deliver.Received(1).Handle(Arg.Is<DeliverOrderItemCommand>(c => c.OrderItemId == 1), Arg.Any<CancellationToken>());
        steak.ConsumedByTick.Should().Be(Now + 18); // roll 0.0 -> the 18 s minimum for a main
    }

    [Fact]
    public async Task QueuedPickupDrink_IsAttempted_ButAQueuedStationItemIsLeftToTheKitchen()
    {
        var water = Item(1, 1, 1, ItemPhase.Drinks, OrderItemStatus.Queued);
        var steak = Item(2, 2, 5, ItemPhase.EntryMain, OrderItemStatus.Queued);
        var (restaurant, _) = Scenario(DiningVisit.Refill, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.EntryMain, items: new[] { water, steak });

        await Run(restaurant);

        await _deliver.Received(1).Handle(Arg.Is<DeliverOrderItemCommand>(c => c.OrderItemId == 1), Arg.Any<CancellationToken>());
        await _deliver.DidNotReceive().Handle(Arg.Is<DeliverOrderItemCommand>(c => c.OrderItemId == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedDelivery_IsRetriedSilently_NoConsumeTimerAndNoReportedFailure()
    {
        _deliver.Handle(Arg.Any<DeliverOrderItemCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure("no idle waiter"));
        var steak = Item(1, 1, 5, ItemPhase.EntryMain, OrderItemStatus.Ready);
        var (restaurant, _) = Scenario(DiningVisit.Refill, GroupPhaseWaitState.Serving, nextTick: 0, course: CoursePhase.EntryMain, items: new[] { steak });

        var result = await Run(restaurant);

        result.IsSuccess.Should().BeTrue();
        steak.ConsumedByTick.Should().BeNull();
    }

    [Fact]
    public async Task NoActiveGroups_DoesNothingAndSkipsTheMenuLookup()
    {
        var (restaurant, group) = Scenario(DiningVisit.Drinks, GroupPhaseWaitState.AutoVisitPending, nextTick: Now);
        restaurant.CustomerService.ActiveGroups.Remove(group);

        var result = await Run(restaurant);

        result.IsSuccess.Should().BeTrue();
        await _menuRepository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    // ---------- helpers ----------

    private async Task<Result> Run(Restaurant restaurant)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        return await _handler.Handle(new AdvanceDiningCommand(restaurant.RestaurantId), CancellationToken.None);
    }

    private (Restaurant Restaurant, CustomerGroup Group) Scenario(
        DiningVisit nextVisit, GroupPhaseWaitState wait, long nextTick,
        CoursePhase course = CoursePhase.Seated, int attempt = 0, IReadOnlyList<OrderItem>? items = null,
        int ticksPerRealSecond = 1, StaffState waiterState = StaffState.Idle)
    {
        var group = new CustomerGroup
        {
            GroupId = 1, PartySize = 2, ArrivalTick = 0, ClusterId = 10,
            Members = new List<Customer> { Person(1), Person(2) },
            LifecycleState = GroupLifecycleState.Seated, CoursePhase = course,
            PhaseWaitState = wait, NextVisit = nextVisit, NextAutoVisitTick = nextTick, VisitAttempt = attempt
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = Now,
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };

        restaurant.StaffRoster.Staff.Add(new StaffMember
        {
            StaffId = 1, Name = "Waiter A", Role = StaffRole.Waiter, State = waiterState,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        });
        restaurant.CustomerService.ActiveGroups.Add(group);

        if (items is not null)
        {
            var order = new Order { OrderId = 1, GroupId = 1, ClusterId = 10, Items = items.ToList(), CreatedAtTick = 0 };
            restaurant.KitchenOps.ActiveOrders.Add(order);
            group.CurrentOrderId = order.OrderId;
        }

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig(ticksPerRealSecond));
        return (restaurant, group);
    }

    private static OrderItem Item(int id, int owner, int menuItemId, ItemPhase phase, OrderItemStatus status, long? consumedBy = null) => new()
    {
        OrderItemId = id, OwnerId = owner, MenuItemId = menuItemId, Phase = phase, PriceAtOrder = 1m,
        Status = status, CreatedAtTick = 0, ConsumedByTick = consumedBy
    };

    private static Customer Person(int id) => new()
    {
        CustomerId = id, IsAdult = true, Mood = 60, PatienceLevel = 0.5f, Instability = 0.5f,
        SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
    };

    private static RestaurantConfig BuildConfig(int ticksPerRealSecond) => new()
    {
        ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = ticksPerRealSecond,
        TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
        MenuItemIds = new[] { 1, 5, 8 }, ReputationStart = 20f,
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
