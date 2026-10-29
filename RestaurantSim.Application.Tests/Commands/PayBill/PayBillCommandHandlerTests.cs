using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.PayBill;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.PayBill;

public class PayBillCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly PayBillCommandHandler _handler;

    public PayBillCommandHandlerTests()
    {
        _handler = new PayBillCommandHandler(_repository, _presetRepository, _publisher);
    }

    [Fact]
    public async Task Handle_PaysBill_WhenAllItemsServedAsync()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Dessert, payerMood: 80, allServed: true);
        SetupConfig(restaurant);
        var cashBefore = restaurant.Cash;

        var result = await _handler.Handle(new PayBillCommand(restaurant.RestaurantId, group.GroupId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        group.CoursePhase.Should().Be(CoursePhase.Done);
        group.LifecycleState.Should().Be(GroupLifecycleState.Leaving);
        restaurant.Cash.Should().Be(cashBefore + 12.50m); // order total added, tip NOT added to cash

        await _publisher.Received(1).Publish(
            Arg.Is<BillPaid>(e => e.Revenue == 12.50m && e.GroupId == group.GroupId && e.CashAfter == cashBefore + 12.50m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AllowsPayment_AfterEntryMain_WithoutDessertAsync()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.EntryMain, payerMood: 80, allServed: true);
        SetupConfig(restaurant);

        var result = await _handler.Handle(new PayBillCommand(restaurant.RestaurantId, group.GroupId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(); // skipping dessert is allowed
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenNotAllItemsServedAsync()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Dessert, payerMood: 80, allServed: false);
        SetupConfig(restaurant);

        var result = await _handler.Handle(new PayBillCommand(restaurant.RestaurantId, group.GroupId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        group.LifecycleState.Should().NotBe(GroupLifecycleState.Leaving);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenCoursePhaseIsBeforeEntryMainAsync()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Drinks, payerMood: 80, allServed: true);
        SetupConfig(restaurant);

        var result = await _handler.Handle(new PayBillCommand(restaurant.RestaurantId, group.GroupId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_HigherTip_WhenPayerMoodIsHigherAsync()
    {
        var (restaurantLowMood, groupLowMood) = BuildScenario(coursePhase: CoursePhase.Dessert, payerMood: 20, allServed: true);
        SetupConfig(restaurantLowMood);
        var (restaurantHighMood, groupHighMood) = BuildScenario(coursePhase: CoursePhase.Dessert, payerMood: 90, allServed: true);
        SetupConfig(restaurantHighMood);

        BillPaid? lowMoodEvent = null;
        BillPaid? highMoodEvent = null;
        _publisher.Publish(Arg.Do<BillPaid>(e => lowMoodEvent = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.Handle(new PayBillCommand(restaurantLowMood.RestaurantId, groupLowMood.GroupId), CancellationToken.None);
        var lowTip = lowMoodEvent!.TipAmount;

        _publisher.ClearReceivedCalls();
        _publisher.Publish(Arg.Do<BillPaid>(e => highMoodEvent = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        await _handler.Handle(new PayBillCommand(restaurantHighMood.RestaurantId, groupHighMood.GroupId), CancellationToken.None);
        var highTip = highMoodEvent!.TipAmount;

        highTip.Should().BeGreaterThan(lowTip);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new PayBillCommand(Guid.NewGuid(), 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private void SetupConfig(Restaurant restaurant)
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
            MenuItemIds = new[] { 5 }, ReputationStart = 20f, QueueWaitMoodDecayPerSecond = -0.4f,
            CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f, ProbabilityCheckpointIntervalSeconds = 3f,
            PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<MealPeriodDefinition>(), EventModuleIds = Array.Empty<string>(),
            TipBaseRate = 50f, TipMoodExponent = 0.75f, MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
    }

    private static (Restaurant Restaurant, CustomerGroup Group) BuildScenario(CoursePhase coursePhase, float payerMood, bool allServed)
    {
        var customer = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = payerMood, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
        };

        var item = new OrderItem
        {
            OrderItemId = 1, OwnerId = 1, MenuItemId = 5, Phase = ItemPhase.EntryMain, PriceAtOrder = 12.50m,
            Status = allServed ? OrderItemStatus.Served : OrderItemStatus.Cooking, CreatedAtTick = 0
        };

        var order = new Order { OrderId = 1, GroupId = 1, ClusterId = 10, Items = new List<OrderItem> { item }, CreatedAtTick = 0 };

        var group = new CustomerGroup
        {
            GroupId = 1, PartySize = 1, Members = new List<Customer> { customer }, ArrivalTick = 0,
            ClusterId = 10, LifecycleState = GroupLifecycleState.Seated, CoursePhase = coursePhase,
            CurrentOrderId = order.OrderId, PayerId = customer.CustomerId
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", Cash = 5000m,
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };

        restaurant.CustomerService.ActiveGroups.Add(group);
        restaurant.KitchenOps.ActiveOrders.Add(order);

        return (restaurant, group);
    }
}
