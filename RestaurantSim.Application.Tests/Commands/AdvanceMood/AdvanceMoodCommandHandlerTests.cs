using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceMood;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceMood;

public class AdvanceMoodCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly AdvanceMoodCommandHandler _handler;

    public AdvanceMoodCommandHandlerTests()
    {
        _handler = new AdvanceMoodCommandHandler(_repository, _presetRepository);
    }

    [Fact]
    public async Task Handle_DecaysMood_ForQueuedCustomersAsync()
    {
        var restaurant = BuildRestaurant();
        var customer = AddQueuedCustomer(restaurant, patienceLevel: 0.5f, mood: 60);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        // DecayModifier(0.5) = 1.6-1.2*0.5 = 1.0, baseline=1.0 -> modifier=1.0
        // decayRate = -0.4 * 1.0 = -0.4
        customer.Mood.Should().Be(60f - 0.4f);
    }

    [Fact]
    public async Task Handle_RecoversMood_ForSeatedCustomerWithNoOutstandingItemsAsync()
    {
        var restaurant = BuildRestaurant();
        var customer = AddSeatedCustomer(restaurant, patienceLevel: 0.5f, mood: 60, hasOutstandingItem: false);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        // RecoveryModifier(0.5) = 0.4+1.2*0.5 = 1.0
        // recoveryRate = 0.5 * 1.0 = 0.5
        customer.Mood.Should().Be(60f + 0.5f);
    }

    [Fact]
    public async Task Handle_DecaysMood_ForSeatedCustomerWaitingOnCourseAsync()
    {
        var restaurant = BuildRestaurant();
        var customer = AddSeatedCustomer(restaurant, patienceLevel: 0.5f, mood: 60, hasOutstandingItem: true);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        // decayRate = -0.3 * 1.0 = -0.3 (CourseWaitMoodDecayPerSecond)
        customer.Mood.Should().Be(60f - 0.3f);
    }

    [Fact]
    public async Task Handle_SkipsAbsentCustomers_MoodFrozenAsync()
    {
        var restaurant = BuildRestaurant();
        var customer = AddSeatedCustomer(restaurant, patienceLevel: 0.5f, mood: 60, hasOutstandingItem: false);
        customer.IsPresentAtTable = false;
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        customer.Mood.Should().Be(60f); // unchanged
    }

    [Fact]
    public async Task Handle_HigherPatience_DecaysSlowerAsync()
    {
        var restaurantLowPatience = BuildRestaurant();
        var lowPatienceCustomer = AddQueuedCustomer(restaurantLowPatience, patienceLevel: 0.1f, mood: 60);
        SetupConfig(restaurantLowPatience);

        var restaurantHighPatience = BuildRestaurant();
        var highPatienceCustomer = AddQueuedCustomer(restaurantHighPatience, patienceLevel: 0.9f, mood: 60);
        SetupConfig(restaurantHighPatience);

        await _handler.Handle(new AdvanceMoodCommand(restaurantLowPatience.RestaurantId), CancellationToken.None);
        await _handler.Handle(new AdvanceMoodCommand(restaurantHighPatience.RestaurantId), CancellationToken.None);

        // Low patience decays MORE (bigger drop) than high patience.
        (60f - lowPatienceCustomer.Mood).Should().BeGreaterThan(60f - highPatienceCustomer.Mood);
    }

    [Fact]
    public async Task Handle_ClampsMoodWithinZeroToHundredAsync()
    {
        var restaurant = BuildRestaurant();
        var customer = AddSeatedCustomer(restaurant, patienceLevel: 0.9f, mood: 99.8f, hasOutstandingItem: false);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        customer.Mood.Should().BeLessThanOrEqualTo(100f);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new AdvanceMoodCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private void SetupConfig(Restaurant restaurant)
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
            MenuItemIds = Array.Empty<int>(), ReputationStart = 20f,
            QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f,
            ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<MealPeriodDefinition>(), EventModuleIds = Array.Empty<string>(),
            TipBaseRate = 50f, TipMoodExponent = 0.75f, MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f,
            PatienceDecayBaseline = 1.0f
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
    }

    private static Customer AddQueuedCustomer(Restaurant restaurant, float patienceLevel, float mood)
    {
        var customer = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = mood, PatienceLevel = patienceLevel, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
        };

        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = 1, PartySize = 1, Members = new List<Customer> { customer }, ArrivalTick = 0
        });

        return customer;
    }

    private static Customer AddSeatedCustomer(Restaurant restaurant, float patienceLevel, float mood, bool hasOutstandingItem)
    {
        var customer = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = mood, PatienceLevel = patienceLevel, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Male, IsPresentAtTable = true
        };

        var order = new Order
        {
            OrderId = 1, GroupId = 1, ClusterId = 10,
            Items = hasOutstandingItem
                ? new List<OrderItem> { new() { OrderItemId = 1, OwnerId = 1, MenuItemId = 5, Phase = ItemPhase.EntryMain, PriceAtOrder = 10m, Status = OrderItemStatus.Cooking, CreatedAtTick = 0 } }
                : new List<OrderItem>(),
            CreatedAtTick = 0
        };
        restaurant.KitchenOps.ActiveOrders.Add(order);

        restaurant.CustomerService.ActiveGroups.Add(new CustomerGroup
        {
            GroupId = 1, PartySize = 1, Members = new List<Customer> { customer }, ArrivalTick = 0,
            ClusterId = 10, LifecycleState = GroupLifecycleState.Seated, CurrentOrderId = 1
        });

        return customer;
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(),
        ConfigId = "default_diner",
        Floor = new FloorState(),
        StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(),
        KitchenOps = new KitchenOpsState(),
        Events = new EventState()
    };
}
