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
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.DeliverOrderItem;

public class DeliverOrderItemCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly DeliverOrderItemCommandHandler _handler;

    public DeliverOrderItemCommandHandlerTests()
    {
        _handler = new DeliverOrderItemCommandHandler(_repository, _publisher);
    }

    [Fact]
    public async Task Handle_DeliversReadyItem_HighQuality_AppliesPositiveMoodBonus()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: true, hadFoodQualityEvent: false);
        var moodBefore = group.Members[0].Mood;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Served);
        group.Members[0].Mood.Should().Be(moodBefore + 10); // QualityAtServe=100 -> +10

        await _publisher.Received(1).Publish(
            Arg.Is<OrderItemServed>(e => e.QualityAtServe == 100f && e.MoodBonus == 10f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeliversReadyItem_FoodQualityEvent_AppliesNegativeMoodBonus()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: true);
        var moodBefore = group.Members[0].Mood;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        // BaseQuality=30 (FoodQualityEvent, no hold) -> QualityAtServe=30 -> MoodBonus=-8
        group.Members[0].Mood.Should().Be(moodBefore - 8);
    }

    [Fact]
    public async Task Handle_DeliversReadyItem_StandardQuality_NoMoodChange()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: false);
        var moodBefore = group.Members[0].Mood;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        // BaseQuality=80 (standard, no hold) -> QualityAtServe=80 -> MoodBonus=0
        group.Members[0].Mood.Should().Be(moodBefore);
    }

    [Fact]
    public async Task Handle_AppliesHoldDecay_WhenItemWasHeldUnderLamp()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.HeldUnderLamp, isHighQuality: true, hadFoodQualityEvent: false, heldSinceTick: 90);
        restaurant.SimulationTick = 100; // 10 ticks held
        restaurant.Floor.HeatLamps[0].OccupiedSlots.Add(item.OrderItemId);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        // IsHighQuality halves decay rate: EffectiveDecayRate = 1 x 0.5 = 0.5/tick.
        // QualityAtServe = 100 - (10 x 0.5) = 95 -> still >= 90 -> MoodBonus +10.
        item.Status.Should().Be(OrderItemStatus.Served);
        restaurant.Floor.HeatLamps[0].OccupiedSlots.Should().NotContain(item.OrderItemId);

        await _publisher.Received(1).Publish(
            Arg.Is<OrderItemServed>(e => e.QualityAtServe == 95f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenItemIsNotReadyOrHeld()
    {
        var (restaurant, item, _) = BuildScenario(OrderItemStatus.Cooking, isHighQuality: false, hadFoodQualityEvent: false);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Cooking); // unchanged
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenNoIdleWaiterExists()
    {
        var (restaurant, item, _) = BuildScenario(OrderItemStatus.Ready, isHighQuality: false, hadFoodQualityEvent: false);
        restaurant.StaffRoster.Staff[0].State = StaffState.Busy;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Ready); // unchanged
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new DeliverOrderItemCommand(Guid.NewGuid(), 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DoesNotApplyMoodBonus_WhenCustomerIsAbsentFromTable()
    {
        var (restaurant, item, group) = BuildScenario(OrderItemStatus.Ready, isHighQuality: true, hadFoodQualityEvent: false);
        group.Members[0].IsPresentAtTable = false; // e.g. in the bathroom
        var moodBefore = group.Members[0].Mood;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        item.Status.Should().Be(OrderItemStatus.Served); // still delivered
        group.Members[0].Mood.Should().Be(moodBefore); // but no mood change while absent

        // Event still reports what the bonus WOULD have been, for visibility.
        await _publisher.Received(1).Publish(
            Arg.Is<OrderItemServed>(e => e.MoodBonus == 10f),
            Arg.Any<CancellationToken>());
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
            Status = status, IsHighQuality = isHighQuality, HadFoodQualityEvent = hadFoodQualityEvent, HeldSinceTick = heldSinceTick
        };

        var order = new Order { OrderId = 1, GroupId = 1, ClusterId = 10, Items = new List<OrderItem> { item }, CreatedAtTick = 0 };

        var waiter = new StaffMember
        {
            StaffId = 1, Name = "Waiter A", Role = StaffRole.Waiter, State = StaffState.Idle,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        };

        var heatLamp = new HeatLamp { HeatLampId = 1, Position = new GridPosition(4, 0), Hitbox = new GridBounds(1, 1), SlotCount = 3, QualityDecayRatePerTick = 1f };

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
