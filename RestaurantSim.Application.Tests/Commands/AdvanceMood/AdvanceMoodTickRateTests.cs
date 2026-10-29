using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceMood;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceMood;

// Rates are authored per real second: whatever TicksPerRealSecond is, one real
// second of ticks must move Mood by the same amount.
public class AdvanceMoodTickRateTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly AdvanceMoodCommandHandler _handler;

    public AdvanceMoodTickRateTests()
    {
        _handler = new AdvanceMoodCommandHandler(_repository, _presetRepository);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task QueueWaitDecay_OneRealSecondOfTicks_IsTheSameAtAnyTickRateAsync(int ticksPerRealSecond)
    {
        var restaurant = BuildRestaurant();
        var customer = AddQueuedCustomer(restaurant, mood: 60);
        Setup(restaurant, ticksPerRealSecond);

        for (var tick = 0; tick < ticksPerRealSecond; tick++)
            await _handler.Handle(new AdvanceMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        // -0.4 per real second at PatienceLevel 0.5 (modifier 1.0)
        customer.Mood.Should().BeApproximately(60f - 0.4f, 0.0001f);
    }

    [Fact]
    public async Task QueueWaitDecay_SingleTick_ScalesDownWithTheTickRateAsync()
    {
        var restaurant = BuildRestaurant();
        var customer = AddQueuedCustomer(restaurant, mood: 60);
        Setup(restaurant, ticksPerRealSecond: 2);

        await _handler.Handle(new AdvanceMoodCommand(restaurant.RestaurantId), CancellationToken.None);

        customer.Mood.Should().BeApproximately(60f - 0.2f, 0.0001f);
    }

    private void Setup(Restaurant restaurant, int ticksPerRealSecond)
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = ticksPerRealSecond,
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

    private static Customer AddQueuedCustomer(Restaurant restaurant, float mood)
    {
        var customer = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = mood, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
        };

        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = 1, PartySize = 1, Members = new List<Customer> { customer }, ArrivalTick = 0
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
