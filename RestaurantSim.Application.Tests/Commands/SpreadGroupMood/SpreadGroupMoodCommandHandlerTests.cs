using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.SpreadGroupMood;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SpreadGroupMood;

public class SpreadGroupMoodCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly SpreadGroupMoodCommandHandler _handler;

    public SpreadGroupMoodCommandHandlerTests()
    {
        _presetRepository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(BuildConfig(ticksPerRealSecond: 1));
        _handler = new SpreadGroupMoodCommandHandler(_repository, _presetRepository);
    }

    [Fact]
    public async Task Handle_ScalesSpreadRateDownWithTheTickRateAsync()
    {
        _presetRepository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(BuildConfig(ticksPerRealSecond: 2));
        var restaurant = BuildRestaurant();
        var (a, b) = AddSeatedPair(restaurant, moodA: 20, moodB: 80, susceptibilityA: 0.5f, susceptibilityB: 0.5f);

        await _handler.Handle(new SpreadGroupMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        // at 1 tick/s the delta is 1.5 (first test); at 2 ticks/s it is half
        a.Mood.Should().BeApproximately(20.75f, 0.001f);
        b.Mood.Should().BeApproximately(79.25f, 0.001f);
    }

    private static RestaurantConfig BuildConfig(int ticksPerRealSecond) => new()
    {
        ConfigId = "default_diner",
        StartingCash = 5000m,
        GameMinutesPerRealSecond = 1,
        TicksPerRealSecond = ticksPerRealSecond,
        TableLayoutId = "default_diner_layout",
        StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
        MenuItemIds = Array.Empty<int>(),
        ReputationStart = 20f,
        QueueWaitMoodDecayPerSecond = -0.4f,
        CourseWaitMoodDecayPerSecond = -0.3f,
        NaturalMoodRecoveryPerSecond = 0.5f,
        ProbabilityCheckpointIntervalSeconds = 3f,
        PathReplanMaxWaitSeconds = 5f,
        MaxCallAttempts = 3,
        ActiveMealPeriods = new List<RestaurantSim.Domain.Scheduling.MealPeriodDefinition>(),
        EventModuleIds = Array.Empty<string>(),
        TipBaseRate = 50f,
        TipMoodExponent = 0.75f,
        MaxQueueLength = 12,
        BaseArrivalIntervalSeconds = 120f
    };

    [Fact]
    public async Task Handle_MovesTwoAdultsTowardEachOther_AtNormalRateAsync()
    {
        // fullGroupAvgMood=(20+80)/2=50, not tipping -> rate=0.05
        // A: groupWeightedAvg=80 -> delta=0.5*(80-20)*0.05=1.5
        // B: groupWeightedAvg=20 -> delta=0.5*(20-80)*0.05=-1.5
        var restaurant = BuildRestaurant();
        var (a, b) = AddSeatedPair(restaurant, moodA: 20, moodB: 80, susceptibilityA: 0.5f, susceptibilityB: 0.5f);

        await _handler.Handle(new SpreadGroupMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        a.Mood.Should().BeApproximately(21.5f, 0.001f);
        b.Mood.Should().BeApproximately(78.5f, 0.001f);
    }

    [Fact]
    public async Task Handle_UsesTippingPointRate_WhenFullGroupAvgMoodBelowThresholdAsync()
    {
        // fullGroupAvgMood=(10+20)/2=15 < 30 -> rate=0.075
        // A: delta=0.5*(20-10)*0.075=0.375
        var restaurant = BuildRestaurant();
        var (a, _) = AddSeatedPair(restaurant, moodA: 10, moodB: 20, susceptibilityA: 0.5f, susceptibilityB: 0.5f);

        await _handler.Handle(new SpreadGroupMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        a.Mood.Should().BeApproximately(10.375f, 0.001f);
    }

    [Fact]
    public async Task Handle_WeightsAdultInfluenceHigherThanChildAsync()
    {
        // TargetKid's other two members: AdultInfluencer(100, weight 1.5),
        // ChildInfluencer(0, weight 1.0) -> weighted avg = 150/2.5 = 60
        // (a naive unweighted average would be 50 — this proves weighting
        // actually applies, not just an accidental match).
        var restaurant = BuildRestaurant();

        var targetKid = new Customer
        {
            CustomerId = 1, IsAdult = false, Mood = 50, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 1.0f, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Male, IsPresentAtTable = true
        };
        var adultInfluencer = new Customer
        {
            CustomerId = 2, IsAdult = true, Mood = 100, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Male, IsPresentAtTable = true
        };
        var childInfluencer = new Customer
        {
            CustomerId = 3, IsAdult = false, Mood = 0, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Female, IsPresentAtTable = true
        };

        restaurant.CustomerService.ActiveGroups.Add(new CustomerGroup
        {
            GroupId = 1, PartySize = 3, Members = new List<Customer> { targetKid, adultInfluencer, childInfluencer },
            ArrivalTick = 0, ClusterId = 10, LifecycleState = GroupLifecycleState.Seated
        });

        await _handler.Handle(new SpreadGroupMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        // fullGroupAvgMood=(50+100+0)/3=50, not tipping -> rate=0.05
        // delta = 1.0 * (60-50) * 0.05 = 0.5
        targetKid.Mood.Should().BeApproximately(50.5f, 0.001f);
    }

    [Fact]
    public async Task Handle_HigherSusceptibility_ProducesLargerDeltaAsync()
    {
        var restaurantLow = BuildRestaurant();
        var (lowSusceptA, _) = AddSeatedPair(restaurantLow, moodA: 20, moodB: 80, susceptibilityA: 0.1f, susceptibilityB: 0.5f);

        var restaurantHigh = BuildRestaurant();
        var (highSusceptA, _) = AddSeatedPair(restaurantHigh, moodA: 20, moodB: 80, susceptibilityA: 0.9f, susceptibilityB: 0.5f);

        await _handler.Handle(new SpreadGroupMoodCommand(restaurantLow.RestaurantId), CancellationToken.None);
        await _handler.Handle(new SpreadGroupMoodCommand(restaurantHigh.RestaurantId), CancellationToken.None);

        (highSusceptA.Mood - 20f).Should().BeGreaterThan(lowSusceptA.Mood - 20f);
    }

    [Fact]
    public async Task Handle_SkipsSpread_WhenOnlyOnePresentMemberAsync()
    {
        var restaurant = BuildRestaurant();
        var (a, b) = AddSeatedPair(restaurant, moodA: 20, moodB: 80, susceptibilityA: 0.5f, susceptibilityB: 0.5f);
        b.IsPresentAtTable = false; // absent — only 1 present member remains

        await _handler.Handle(new SpreadGroupMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        a.Mood.Should().Be(20f); // unchanged — no spread with <2 present
        b.Mood.Should().Be(80f); // frozen while absent, untouched entirely
    }

    [Fact]
    public async Task Handle_AppliesSpread_ToQueuedGroupsTooAsync()
    {
        var restaurant = BuildRestaurant();
        var a = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = 20, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
        };
        var b = new Customer
        {
            CustomerId = 2, IsAdult = true, Mood = 80, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Female
        };
        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = 1, PartySize = 2, Members = new List<Customer> { a, b }, ArrivalTick = 0
        });

        await _handler.Handle(new SpreadGroupMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        a.Mood.Should().BeApproximately(21.5f, 0.001f);
    }

    [Fact]
    public async Task Handle_ClampsMoodWithinZeroToHundredAsync()
    {
        var restaurant = BuildRestaurant();
        var (a, _) = AddSeatedPair(restaurant, moodA: 99.9f, moodB: 100f, susceptibilityA: 1.0f, susceptibilityB: 1.0f);

        await _handler.Handle(new SpreadGroupMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        a.Mood.Should().BeLessThanOrEqualTo(100f);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new SpreadGroupMoodCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static (Customer A, Customer B) AddSeatedPair(Restaurant restaurant, float moodA, float moodB, float susceptibilityA, float susceptibilityB)
    {
        var a = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = moodA, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = susceptibilityA, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Male, IsPresentAtTable = true
        };
        var b = new Customer
        {
            CustomerId = 2, IsAdult = true, Mood = moodB, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = susceptibilityB, SpendingTendency = SpendingTendency.Normal, IsRegular = false,
            Gender = Gender.Female, IsPresentAtTable = true
        };

        restaurant.CustomerService.ActiveGroups.Add(new CustomerGroup
        {
            GroupId = 1, PartySize = 2, Members = new List<Customer> { a, b },
            ArrivalTick = 0, ClusterId = 10, LifecycleState = GroupLifecycleState.Seated
        });

        return (a, b);
    }

    private Restaurant BuildRestaurant()
    {
        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        return restaurant;
    }
}
