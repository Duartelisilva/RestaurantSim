using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.PlaceOrder;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.PlaceOrder;

public class PlaceOrderCommandValidatorTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IMenuRepository _menuRepository = Substitute.For<IMenuRepository>();
    private readonly PlaceOrderCommandValidator _validator;

    private static readonly MenuItem WaterItem = new()
    {
        MenuItemId = 1, Name = "Water", Category = MenuItemCategory.Adult, Course = ItemPhase.Drinks,
        RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 1.50m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    private static readonly MenuItem KidsJuice = new()
    {
        MenuItemId = 4, Name = "Orange Juice", Category = MenuItemCategory.Kids, Course = ItemPhase.Drinks,
        RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 2.00m,
        AvailableMealPeriods = new[] { "lunch" }
    };

    public PlaceOrderCommandValidatorTests()
    {
        _validator = new PlaceOrderCommandValidator(_repository, _presetRepository, _menuRepository);
        _menuRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(WaterItem);
        _menuRepository.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(KidsJuice);
    }

    [Fact]
    public async Task Validate_Succeeds_ForFirstDrinksOrder()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Seated);
        SetupRepositories(restaurant);

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 1) }));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Fails_WhenOrderingMainBeforeDrinks()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Seated); // never ordered drinks
        SetupRepositories(restaurant);

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.EntryMain,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 5) }));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Succeeds_ForDrinksRefill_MidMeal()
    {
        // Drinks can be requested any time before the bill (business logic §1.4).
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Dessert);
        SetupRepositories(restaurant);

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 1) }));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Fails_WhenGroupIsNotActive()
    {
        var (restaurant, _) = BuildScenario(coursePhase: CoursePhase.Seated);
        SetupRepositories(restaurant);

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, GroupId: 999, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(1, 1) }));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Fails_WhenKidOrdersAdultItem()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Seated, secondMemberIsKid: true);
        SetupRepositories(restaurant);
        var kid = group.Members[1];

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(kid.CustomerId, 1) })); // Water is Adult category

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Succeeds_WhenKidOrdersKidsItem()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Seated, secondMemberIsKid: true);
        SetupRepositories(restaurant);
        var kid = group.Members[1];

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(kid.CustomerId, 4) })); // Orange Juice is Kids category

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Fails_WhenCustomerIsNotAMemberOfTheGroup()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Seated);
        SetupRepositories(restaurant);

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(CustomerId: 9999, 1) }));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Fails_WhenItemNotOnRestaurantMenu()
    {
        var (restaurant, group) = BuildScenario(coursePhase: CoursePhase.Seated);
        SetupRepositories(restaurant, menuItemIds: new[] { 4 }); // restaurant only serves item 4, not 1

        var result = await _validator.ValidateAsync(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks,
                new[] { new PlaceOrderItemRequest(group.Members[0].CustomerId, 1) }));

        result.IsValid.Should().BeFalse();
    }

    private static (Restaurant Restaurant, CustomerGroup Group) BuildScenario(CoursePhase coursePhase, bool secondMemberIsKid = false)
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

        var group = new CustomerGroup
        {
            GroupId = 1,
            PartySize = members.Count,
            Members = members,
            ArrivalTick = 0,
            ClusterId = 10,
            LifecycleState = GroupLifecycleState.Seated,
            CoursePhase = coursePhase
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            SimulationTick = 750, // 12:30, within lunch
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        restaurant.CustomerService.ActiveGroups.Add(group);
        return (restaurant, group);
    }

    private void SetupRepositories(Restaurant restaurant, int[]? menuItemIds = null)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var config = new RestaurantConfig
        {
            ConfigId = "default_diner",
            StartingCash = 5000m,
            MinutesPerTick = 1,
            TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout",
            StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
            MenuItemIds = menuItemIds ?? new[] { 1, 4, 5 },
            ReputationStart = 20f,
            QueueWaitMoodDecayRate = -0.4f,
            CourseWaitMoodDecayRate = -0.3f,
            NaturalMoodRecoveryRate = 0.5f,
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
}
