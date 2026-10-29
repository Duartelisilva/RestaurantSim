using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.DeliverOrderItem;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.DeliverOrderItem;

public class DeliverOrderItemCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IMenuRepository _menuRepository = Substitute.For<IMenuRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly DeliverOrderItemCommandHandler _handler;

    private static readonly MenuItem SteakItem = new()
    {
        MenuItemId = 5, Name = "Grilled Steak", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain,
        RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 14, DefaultPrice = 12.50m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    private static readonly MenuItem WaterItem = new()
    {
        MenuItemId = 1, Name = "Water", Category = MenuItemCategory.Adult, Course = ItemPhase.Drinks,
        RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 1.50m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    public DeliverOrderItemCommandHandlerTests()
    {
        _handler = new DeliverOrderItemCommandHandler(_repository, _presetRepository, _menuRepository, _publisher);
        _menuRepository.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(SteakItem);
        _menuRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(WaterItem);
    }

    [Fact]
    public async Task Handle_DeliversReadyItem_HighQuality_AppliesPositiveMoodBonusAsync()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: true, hadFoodQualityEvent: false);
        var moodBefore = group.Members[0].Mood;
        SetupRepos(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Served);
        group.Members[0].Mood.Should().Be(moodBefore + 10);

        await _publisher.Received(1).Publish(
            Arg.Is<OrderItemServed>(e => e.QualityAtServe == 100f && e.MoodBonus == 10f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeliversReadyItem_FoodQualityEvent_AppliesNegativeMoodBonusAsync()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: true);
        var moodBefore = group.Members[0].Mood;
        SetupRepos(restaurant);

        await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        group.Members[0].Mood.Should().Be(moodBefore - 8);
    }

    [Fact]
    public async Task Handle_DeliversReadyItem_StandardQuality_NoMoodChangeAsync()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: false);
        var moodBefore = group.Members[0].Mood;
        SetupRepos(restaurant);

        await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        group.Members[0].Mood.Should().Be(moodBefore);
    }

    [Fact]
    public async Task Handle_AppliesHoldDecay_WhenItemWasHeldUnderLampAsync()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.HeldUnderLamp, isHighQuality: true, hadFoodQualityEvent: false, heldSinceTick: 90);
        restaurant.SimulationTick = 100;
        restaurant.Floor.HeatLamps[0].OccupiedSlots.Add(item.OrderItemId);
        SetupRepos(restaurant);

        await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        item.Status.Should().Be(OrderItemStatus.Served);
        restaurant.Floor.HeatLamps[0].OccupiedSlots.Should().NotContain(item.OrderItemId);

        await _publisher.Received(1).Publish(
            Arg.Is<OrderItemServed>(e => e.QualityAtServe == 95f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenStationItemIsNotReadyOrHeldAsync()
    {
        var (restaurant, item, _) = BuildScenario(OrderItemStatus.Cooking, isHighQuality: false, hadFoodQualityEvent: false);
        SetupRepos(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Cooking);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenNoIdleWaiterExistsAsync()
    {
        var (restaurant, item, _) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: false);
        restaurant.StaffRoster.Staff[0].State = StaffState.Busy;
        SetupRepos(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Ready);
    }

    [Fact]
    public async Task Handle_DoesNotApplyMoodBonus_WhenCustomerIsAbsentFromTableAsync()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: true, hadFoodQualityEvent: false);
        group.Members[0].IsPresentAtTable = false;
        var moodBefore = group.Members[0].Mood;
        SetupRepos(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Served);
        group.Members[0].Mood.Should().Be(moodBefore);

        await _publisher.Received(1).Publish(
            Arg.Is<OrderItemServed>(e => e.MoodBonus == 10f),
            Arg.Any<CancellationToken>());
    }

    // --- New: pickup items (Drinks) — no station/cook, flat fetch-time gate ---

    [Fact]
    public async Task Handle_DeliversPickupItem_WhenFetchTimeHasElapsedAsync()
    {
        var (restaurant, _, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: false);
        var pickupItem = new OrderItem
        {
            OrderItemId = 2, OwnerId = 1, MenuItemId = 1, Phase = ItemPhase.Drinks, PriceAtOrder = 1.50m,
            Status = OrderItemStatus.Queued, CreatedAtTick = 90 // Water: PrepTimeSeconds=2 -> ready at tick 92
        };
        restaurant.KitchenOps.ActiveOrders[0].Items.Add(pickupItem);
        restaurant.SimulationTick = 92;
        SetupRepos(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, pickupItem.OrderItemId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        pickupItem.Status.Should().Be(OrderItemStatus.Served);
        await _publisher.Received(1).Publish(
            Arg.Is<OrderItemServed>(e => e.OrderItemId == pickupItem.OrderItemId && e.QualityAtServe == 80f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenPickupItemFetchTimeNotElapsedAsync()
    {
        var (restaurant, _, _) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: false);
        var pickupItem = new OrderItem
        {
            OrderItemId = 2, OwnerId = 1, MenuItemId = 1, Phase = ItemPhase.Drinks, PriceAtOrder = 1.50m,
            Status = OrderItemStatus.Queued, CreatedAtTick = 90
        };
        restaurant.KitchenOps.ActiveOrders[0].Items.Add(pickupItem);
        restaurant.SimulationTick = 91; // only 1 tick elapsed, needs 2
        SetupRepos(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, pickupItem.OrderItemId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        pickupItem.Status.Should().Be(OrderItemStatus.Queued);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new DeliverOrderItemCommand(Guid.NewGuid(), 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private void SetupRepos(Restaurant restaurant)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
            MenuItemIds = new[] { 1, 5 }, ReputationStart = 20f, QueueWaitMoodDecayPerSecond = -0.4f,
            CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f, ProbabilityCheckpointIntervalSeconds = 3f,
            PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<MealPeriodDefinition>(), EventModuleIds = Array.Empty<string>(),
            TipBaseRate = 50f, TipMoodExponent = 0.75f, MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
    }

    private static (Restaurant Restaurant, OrderItem Item, CustomerGroup Group) BuildScenario(
        OrderItemStatus status, bool isHighQuality, bool hadFoodQualityEvent, long? heldSinceTick = null)
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
            Status = status, IsHighQuality = isHighQuality, HadFoodQualityEvent = hadFoodQualityEvent,
            HeldSinceTick = heldSinceTick, CreatedAtTick = 0
        };

        var order = new Order { OrderId = 1, GroupId = 1, ClusterId = 10, Items = new List<OrderItem> { item }, CreatedAtTick = 0 };

        var waiter = new StaffMember
        {
            StaffId = 1, Name = "Waiter A", Role = StaffRole.Waiter, State = StaffState.Idle,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        };

        var heatLamp = new HeatLamp { HeatLampId = 1, Position = new GridPosition(4, 0), Hitbox = new GridBounds(1, 1), SlotCount = 3, QualityDecayRatePerSecond = 1f };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = 100,
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };

        restaurant.Floor.HeatLamps.Add(heatLamp);
        restaurant.StaffRoster.Staff.Add(waiter);
        restaurant.CustomerService.ActiveGroups.Add(group);
        restaurant.KitchenOps.ActiveOrders.Add(order);

        return (restaurant, item, group);
    }
}
