using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.PlaceOrder;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.PlaceOrder;

public class PlaceOrderCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IMenuRepository _menuRepository = Substitute.For<IMenuRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly PlaceOrderCommandHandler _handler;

    private static readonly MenuItem WaterItem = new()
    {
        MenuItemId = 1, Name = "Water", Category = MenuItemCategory.Adult, Course = ItemPhase.Drinks,
        RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 1.50m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    private static readonly MenuItem SteakItem = new()
    {
        MenuItemId = 5, Name = "Grilled Steak", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain,
        RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 14, DefaultPrice = 12.50m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    public PlaceOrderCommandHandlerTests()
    {
        _handler = new PlaceOrderCommandHandler(_repository, _presetRepository, _menuRepository, _publisher);
        _menuRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(WaterItem);
        _menuRepository.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(SteakItem);
    }

    [Fact]
    public async Task Handle_CreatesNewOrder_OnFirstDrinksOrderAsync()
    {
        var restaurant = BuildRestaurant(out var group, clusterId: 10);
        SetupConfig(restaurant);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 1) }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.KitchenOps.ActiveOrders.Should().HaveCount(1);
        group.CurrentOrderId.Should().Be(restaurant.KitchenOps.ActiveOrders[0].OrderId);
        group.CoursePhase.Should().Be(CoursePhase.Drinks);

        var order = restaurant.KitchenOps.ActiveOrders[0];
        order.ClusterId.Should().Be(10);
        order.Items.Should().ContainSingle(i => i.MenuItemId == 1 && i.PriceAtOrder == 1.50m);

        await _publisher.Received(1).Publish(Arg.Any<OrderPlaced>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppendsToExistingOrder_OnSubsequentPhaseAsync()
    {
        var restaurant = BuildRestaurant(out var group, clusterId: 10);
        SetupConfig(restaurant);
        group.CoursePhase = CoursePhase.Drinks;
        var existingOrder = new Order { OrderId = 1, GroupId = group.GroupId, ClusterId = 10, Items = new List<OrderItem>(), CreatedAtTick = 0 };
        restaurant.KitchenOps.ActiveOrders.Add(existingOrder);
        group.CurrentOrderId = existingOrder.OrderId;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.EntryMain,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 5) }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.KitchenOps.ActiveOrders.Should().HaveCount(1);
        existingOrder.Items.Should().ContainSingle(i => i.MenuItemId == 5);
        group.CoursePhase.Should().Be(CoursePhase.EntryMain);
    }

    [Fact]
    public async Task Handle_DoesNotRegressCoursePhase_OnMidMealDrinksRefillAsync()
    {
        var restaurant = BuildRestaurant(out var group, clusterId: 10);
        SetupConfig(restaurant);
        group.CoursePhase = CoursePhase.Dessert;
        var existingOrder = new Order { OrderId = 1, GroupId = group.GroupId, ClusterId = 10, Items = new List<OrderItem>(), CreatedAtTick = 0 };
        restaurant.KitchenOps.ActiveOrders.Add(existingOrder);
        group.CurrentOrderId = existingOrder.OrderId;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 1) }),
            CancellationToken.None);

        group.CoursePhase.Should().Be(CoursePhase.Dessert);
        existingOrder.Items.Should().ContainSingle(i => i.MenuItemId == 1);
    }

    [Fact]
    public async Task Handle_AssignsSequentialOrderItemIdsAsync()
    {
        var restaurant = BuildRestaurant(out var group, clusterId: 10);
        SetupConfig(restaurant);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 1) }),
            CancellationToken.None);

        restaurant.KitchenOps.NextOrderItemId.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenGroupIsNotActiveAsync()
    {
        var restaurant = BuildRestaurant(out _, clusterId: 10);
        SetupConfig(restaurant);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, GroupId: 999, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(1, 1) }),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(
            new PlaceOrderCommand(Guid.NewGuid(), 1, ItemPhase.Drinks, new[] { new PlaceOrderItemRequest(1, 1) }),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    // --- New: proves the handler's own defense actually works, called directly,
    // bypassing the validator entirely. This is the whole point of the refactor. ---

    [Fact]
    public async Task Handle_RejectsInvalidPhaseTransition_EvenWithoutTheValidatorAsync()
    {
        var restaurant = BuildRestaurant(out var group, clusterId: 10);
        SetupConfig(restaurant);
        // CoursePhase is still Seated — EntryMain must be rejected without ever
        // having gone through Drinks first, per business logic §1.4's fixed sequence.
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.EntryMain,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 5) }),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        restaurant.KitchenOps.ActiveOrders.Should().BeEmpty(); // nothing committed
    }

    [Fact]
    public async Task Handle_RejectsKidsMenuViolation_EvenWithoutTheValidatorAsync()
    {
        var restaurant = BuildRestaurant(out var group, clusterId: 10, secondMemberIsKid: true);
        SetupConfig(restaurant);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        var kid = group.Members[1];

        var result = await _handler.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(kid.CustomerId, 1) }), // Water is Adult category
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        restaurant.KitchenOps.ActiveOrders.Should().BeEmpty();
    }

    private void SetupConfig(Restaurant restaurant)
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner",
            StartingCash = 5000m,
            GameMinutesPerRealSecond = 1,
            TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout",
            StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
            MenuItemIds = new[] { 1, 4, 5 },
            ReputationStart = 20f,
            QueueWaitMoodDecayPerSecond = -0.4f,
            CourseWaitMoodDecayPerSecond = -0.3f,
            NaturalMoodRecoveryPerSecond = 0.5f,
            ProbabilityCheckpointIntervalSeconds = 3f,
            PathReplanMaxWaitSeconds = 5f,
            MaxCallAttempts = 3,
            ActiveMealPeriods = new List<MealPeriodDefinition>
            {
                new() { MealPeriodId = "lunch", StartTime = new TimeOfDay(12, 0), EndTime = new TimeOfDay(15, 0) }
            },
            EventModuleIds = Array.Empty<string>(),
            TipBaseRate = 50f,
            TipMoodExponent = 0.75f,
            MaxQueueLength = 12,
            BaseArrivalIntervalSeconds = 120f
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
    }

    private static Restaurant BuildRestaurant(out CustomerGroup group, int clusterId, bool secondMemberIsKid = false)
    {
        var adult = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = 60, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
        };

        var members = new List<Customer> { adult };
        if (secondMemberIsKid)
        {
            members.Add(new Customer
            {
                CustomerId = 2, IsAdult = false, Mood = 60, PatienceLevel = 0.5f, Instability = 0.5f,
                SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Female
            });
        }

        group = new CustomerGroup
        {
            GroupId = 1,
            PartySize = members.Count,
            Members = members,
            ArrivalTick = 0,
            ClusterId = clusterId,
            LifecycleState = GroupLifecycleState.Seated,
            CoursePhase = CoursePhase.Seated
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            SimulationTick = 750,
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState(),
        };

        restaurant.CustomerService.ActiveGroups.Add(group);
        return restaurant;
    }
}
