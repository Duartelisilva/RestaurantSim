using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceKitchen;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceKitchen;

public class AdvanceKitchenCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IMenuRepository _menuRepository = Substitute.For<IMenuRepository>();
    private readonly IRandomProvider _random = Substitute.For<IRandomProvider>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly AdvanceKitchenCommandHandler _handler;

    // PrepTimeSeconds=10, RequiredStationType=grill, baseline cook stats (Speed=0.5,
    // ErrorRate=0, Fatigue=0, both multipliers=1.0) => EffectivePrepTicks = 10.
    private static readonly MenuItem SteakItem = new()
    {
        MenuItemId = 5, Name = "Grilled Steak", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain,
        RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 10, DefaultPrice = 12.50m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    public AdvanceKitchenCommandHandlerTests()
    {
        _handler = new AdvanceKitchenCommandHandler(_repository, _presetRepository, _menuRepository, _random, _publisher);
        _menuRepository.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(SteakItem);
        _random.NextDouble().Returns(0.99); // forces IsHighQuality=false by default in most tests
    }

    [Fact]
    public async Task Handle_StartsCookingItem_WhenStationAndCookAvailableAndPlannedStartTickReached()
    {
        var (restaurant, item, _, station, cook) = BuildQueuedItemScenario(simulationTick: 100, plannedStartTick: 100);
        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        item.Status.Should().Be(OrderItemStatus.Cooking);
        item.StationId.Should().Be(station.StationId);
        item.CookingStartedAtTick.Should().Be(100);
        cook.State.Should().Be(StaffState.Busy);
        cook.CurrentTaskTargetId.Should().Be(item.OrderItemId);
        station.OccupiedSlots.Should().Contain(item.OrderItemId);
    }

    [Fact]
    public async Task Handle_DoesNotStart_WhenPlannedStartTickNotYetReached()
    {
        var (restaurant, item, _, _, _) = BuildQueuedItemScenario(simulationTick: 100, plannedStartTick: 105);
        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        item.Status.Should().Be(OrderItemStatus.Queued);
    }

    [Fact]
    public async Task Handle_DoesNotStart_WhenStationIsFull()
    {
        var (restaurant, item, _, station, _) = BuildQueuedItemScenario(simulationTick: 100, plannedStartTick: 100, stationCapacity: 1);
        station.OccupiedSlots.Add(999);
        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        item.Status.Should().Be(OrderItemStatus.Queued);
    }

    [Fact]
    public async Task Handle_DoesNotStart_WhenNoIdleCook()
    {
        var (restaurant, item, _, _, cook) = BuildQueuedItemScenario(simulationTick: 100, plannedStartTick: 100);
        cook.State = StaffState.Busy;
        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        item.Status.Should().Be(OrderItemStatus.Queued);
    }

    [Fact]
    public async Task Handle_CompletesItem_AfterEffectivePrepTicksElapse_SingleItemTicket()
    {
        var (restaurant, item, _, station, cook) = BuildQueuedItemScenario(simulationTick: 100, plannedStartTick: 90);
        StartCooking(item, station, cook, cookingStartedAtTick: 90); // needs 10 ticks -> done exactly at 100
        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        item.Status.Should().Be(OrderItemStatus.Ready); // single-item ticket, no siblings to sync against
        station.OccupiedSlots.Should().NotContain(item.OrderItemId);
        cook.State.Should().Be(StaffState.Idle);
        cook.CurrentTaskType.Should().BeNull();
        await _publisher.Received(1).Publish(Arg.Any<OrderItemReady>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNotComplete_BeforeEffectivePrepTicksElapse()
    {
        var (restaurant, item, _, station, cook) = BuildQueuedItemScenario(simulationTick: 95, plannedStartTick: 90);
        StartCooking(item, station, cook, cookingStartedAtTick: 90); // needs 10 ticks, only 5 elapsed
        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        item.Status.Should().Be(OrderItemStatus.Cooking);
    }

    [Fact]
    public async Task Handle_RollsHighQuality_WhenRandomRollIsLowEnough()
    {
        var (restaurant, item, _, station, cook) = BuildQueuedItemScenario(simulationTick: 100, plannedStartTick: 90);
        StartCooking(item, station, cook, cookingStartedAtTick: 90);
        SetupRepos(restaurant);
        _random.NextDouble().Returns(0.0001);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        item.IsHighQuality.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_HoldsUnderLamp_WhenSiblingDelayIsShortAndLampHasRoom()
    {
        // ticket.SubmittedAtTick=90, CurrentTick=100 -> WaitPressure=0.5,
        // EffectiveThreshold = 6 x (1-0.5) = 3 (default fallback values).
        // itemA finishes at 100 (started 90). itemB (2nd grill slot) started 93,
        // projects to finish at 103 -> holdTicks = 3 <= 3 -> HOLD.
        var (restaurant, itemA, ticket, station, cookA) = BuildQueuedItemScenario(
            simulationTick: 100, plannedStartTick: 90, ticketSubmittedAtTick: 90, stationCapacity: 2);
        StartCooking(itemA, station, cookA, cookingStartedAtTick: 90);

        var cookB = AddSecondIdleCook(restaurant);
        var (itemB, ticketAfterSibling) = AddSiblingItem(restaurant, ticket, itemId: 20);
        StartCooking(itemB, station, cookB, cookingStartedAtTick: 93);

        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        itemA.Status.Should().Be(OrderItemStatus.HeldUnderLamp);
        itemA.HeldSinceTick.Should().Be(100);
        restaurant.Floor.HeatLamps[0].OccupiedSlots.Should().Contain(itemA.OrderItemId);
        ticketAfterSibling.IsAsyncMode.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReInforms_WhenSiblingDelayIsLong_AndAppliesMoodPenalty()
    {
        // Same threshold math as above (EffectiveThreshold=3). itemB now started
        // at 95 -> projects to finish at 105 -> holdTicks = 5 > 3 -> RE-INFORM.
        var (restaurant, itemA, ticket, station, cookA) = BuildQueuedItemScenario(
            simulationTick: 100, plannedStartTick: 90, ticketSubmittedAtTick: 90, stationCapacity: 2);
        StartCooking(itemA, station, cookA, cookingStartedAtTick: 90);

        var cookB = AddSecondIdleCook(restaurant);
        var (itemB, ticketAfterSibling) = AddSiblingItem(restaurant, ticket, itemId: 20);
        StartCooking(itemB, station, cookB, cookingStartedAtTick: 95);

        var group = restaurant.CustomerService.ActiveGroups[0];
        var moodBefore = group.Members[0].Mood;

        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        itemA.Status.Should().Be(OrderItemStatus.Ready); // served now, not held
        ticketAfterSibling.IsAsyncMode.Should().BeTrue();
        group.Members[0].Mood.Should().BeLessThan(moodBefore); // ReinformMoodPenalty applied
        await _publisher.Received(1).Publish(Arg.Any<TicketReinformed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsHoldEvaluation_WhenTicketAlreadyAsync()
    {
        var (restaurant, itemA, ticket, station, cookA) = BuildQueuedItemScenario(
            simulationTick: 100, plannedStartTick: 90, ticketSubmittedAtTick: 90, stationCapacity: 2);
        ticket.IsAsyncMode = true; // already flipped by an earlier item
        StartCooking(itemA, station, cookA, cookingStartedAtTick: 90);

        var cookB = AddSecondIdleCook(restaurant);
        var (itemB, ticketAfterSibling) = AddSiblingItem(restaurant, ticket, itemId: 20);
        StartCooking(itemB, station, cookB, cookingStartedAtTick: 95); // would otherwise re-inform

        SetupRepos(restaurant);

        await _handler.Handle(new AdvanceKitchenCommand(restaurant.RestaurantId), CancellationToken.None);

        itemA.Status.Should().Be(OrderItemStatus.Ready);
        // No SECOND TicketReinformed/Mood penalty — already async, straight to Ready.
        await _publisher.DidNotReceive().Publish(Arg.Any<TicketReinformed>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new AdvanceKitchenCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    // --- Fixture helpers ---

    private void SetupRepos(Restaurant restaurant)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, MinutesPerTick = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
            MenuItemIds = new[] { 5 }, ReputationStart = 20f, QueueWaitMoodDecayRate = -0.4f,
            CourseWaitMoodDecayRate = -0.3f, NaturalMoodRecoveryRate = 0.5f, ProbabilityCheckpointIntervalSeconds = 3f,
            PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<MealPeriodDefinition>(), EventModuleIds = Array.Empty<string>(),
            TipBaseRate = 50f, TipMoodExponent = 0.75f, MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f
            // BaseHighQualityChance, BaseEventChance, ShortDelayThresholdSeconds,
            // MaxAcceptableWaitSeconds, ReinformMoodPenalty all left null -> handler's
            // documented fallback defaults apply.
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
    }

    private static (Restaurant Restaurant, OrderItem Item, KitchenTicket Ticket, KitchenStation Station, StaffMember Cook) BuildQueuedItemScenario(
        long simulationTick, long plannedStartTick, long ticketSubmittedAtTick = 0, int stationCapacity = 1)
    {
        var customer = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = 60, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Male, IsPresentAtTable = true
        };

        var group = new CustomerGroup
        {
            GroupId = 1, PartySize = 1, Members = new List<Customer> { customer }, ArrivalTick = 0,
            ClusterId = 10, LifecycleState = GroupLifecycleState.Seated, CoursePhase = CoursePhase.EntryMain
        };

        var item = new OrderItem
        {
            OrderItemId = 1, OwnerId = 1, MenuItemId = 5, Phase = ItemPhase.EntryMain, PriceAtOrder = 12.50m,
            Status = OrderItemStatus.Queued, TicketId = 1, PlannedStartTick = plannedStartTick, CreatedAtTick = 0
        };

        var order = new Order { OrderId = 1, GroupId = 1, ClusterId = 10, Items = new List<OrderItem> { item }, CreatedAtTick = 0 };

        var ticket = new KitchenTicket
        {
            TicketId = 1, ClusterId = 10, ItemIds = new List<int> { item.OrderItemId },
            SubmittedAtTick = ticketSubmittedAtTick, TargetReadyTick = ticketSubmittedAtTick + 10
        };

        var stationType = new KitchenStationType { StationTypeId = "grill", Capacity = stationCapacity, SpeedMultiplier = 1.0f };
        var station = new KitchenStation { StationId = 1, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1), StationType = stationType };

        var cookStats = new StaffStats();
        cookStats.Set(StatType.Speed, 0.5f);
        cookStats.Set(StatType.ErrorRate, 0f);
        cookStats.Set(StatType.Fatigue, 0f);
        var cook = new StaffMember { StaffId = 1, Name = "Cook A", Role = StaffRole.Cook, State = StaffState.Idle, Stats = cookStats, AssignedMealPeriods = new[] { "lunch" } };

        var heatLamp = new HeatLamp { HeatLampId = 1, Position = new GridPosition(4, 0), Hitbox = new GridBounds(1, 1), SlotCount = 1, QualityDecayRatePerTick = 1f };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = simulationTick,
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };

        restaurant.Floor.KitchenStations.Add(station);
        restaurant.Floor.HeatLamps.Add(heatLamp);
        restaurant.StaffRoster.Staff.Add(cook);
        restaurant.CustomerService.ActiveGroups.Add(group);
        restaurant.KitchenOps.ActiveOrders.Add(order);
        restaurant.KitchenOps.ActiveTickets.Add(ticket);

        return (restaurant, item, ticket, station, cook);
    }

    private static void StartCooking(OrderItem item, KitchenStation station, StaffMember cook, long cookingStartedAtTick)
    {
        item.Status = OrderItemStatus.Cooking;
        item.StationId = station.StationId;
        item.CookingStartedAtTick = cookingStartedAtTick;
        station.OccupiedSlots.Add(item.OrderItemId);
        cook.State = StaffState.Busy;
        cook.CurrentTaskType = TaskType.CookItem;
        cook.CurrentTaskTargetId = item.OrderItemId;
    }

    private static StaffMember AddSecondIdleCook(Restaurant restaurant)
    {
        var stats = new StaffStats();
        stats.Set(StatType.Speed, 0.5f);
        stats.Set(StatType.ErrorRate, 0f);
        stats.Set(StatType.Fatigue, 0f);
        var cook = new StaffMember { StaffId = 2, Name = "Cook B", Role = StaffRole.Cook, State = StaffState.Idle, Stats = stats, AssignedMealPeriods = new[] { "lunch" } };
        restaurant.StaffRoster.Staff.Add(cook);
        return cook;
    }

    private static (OrderItem Item, KitchenTicket Ticket) AddSiblingItem(Restaurant restaurant, KitchenTicket ticket, int itemId)
    {
        var item = new OrderItem
        {
            OrderItemId = itemId,
            OwnerId = 1,
            MenuItemId = 5,
            Phase = ItemPhase.EntryMain,
            PriceAtOrder = 12.50m,
            TicketId = ticket.TicketId,
            CreatedAtTick = 0
        };
        restaurant.KitchenOps.ActiveOrders[0].Items.Add(item);

        var updatedTicket = new KitchenTicket
        {
            TicketId = ticket.TicketId,
            ClusterId = ticket.ClusterId,
            ItemIds = ticket.ItemIds.Append(itemId).ToList(),
            SubmittedAtTick = ticket.SubmittedAtTick,
            TargetReadyTick = ticket.TargetReadyTick,
            PlayerPrioritized = ticket.PlayerPrioritized,
            IsAsyncMode = ticket.IsAsyncMode
        };

        var index = restaurant.KitchenOps.ActiveTickets.IndexOf(ticket);
        restaurant.KitchenOps.ActiveTickets[index] = updatedTicket;

        return (item, updatedTicket);
    }
}
