using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.CreateRestaurant;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.CreateRestaurant;

public class CreateRestaurantCommandHandlerTests
{
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly ILayoutRepository _layoutRepository = Substitute.For<ILayoutRepository>();
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly CreateRestaurantCommandHandler _handler;

    public CreateRestaurantCommandHandlerTests()
    {
        _handler = new CreateRestaurantCommandHandler(_presetRepository, _layoutRepository, _repository);
    }

    [Fact]
    public async Task Handle_CreatesRestaurant_WithSeededStaffMatchingRosterSeed()
    {
        var config = BuildConfig();
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(config);
        _layoutRepository.GetByIdAsync("default_diner_layout", Arg.Any<CancellationToken>()).Returns(BuildLayout());

        Restaurant? savedRestaurant = null;
        await _repository.SaveAsync(Arg.Do<Restaurant>(r => savedRestaurant = r), Arg.Any<CancellationToken>());

        var result = await _handler.Handle(new CreateRestaurantCommand("default_diner"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        savedRestaurant.Should().NotBeNull();
        savedRestaurant!.StaffRoster.Staff.Should().HaveCount(3); // 1 Host + 2 Waiter per BuildConfig's seed
        savedRestaurant.StaffRoster.Staff.Count(s => s.Role == StaffRole.Waiter).Should().Be(2);
        savedRestaurant.StaffRoster.Staff.Count(s => s.Role == StaffRole.Host).Should().Be(1);
        savedRestaurant.StaffRoster.Staff.Should().OnlyContain(s => s.State == StaffState.Idle);
    }

    [Fact]
    public async Task Handle_AssignsUniqueSequentialStaffIds()
    {
        var config = BuildConfig();
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(config);
        _layoutRepository.GetByIdAsync("default_diner_layout", Arg.Any<CancellationToken>()).Returns(BuildLayout());

        Restaurant? savedRestaurant = null;
        await _repository.SaveAsync(Arg.Do<Restaurant>(r => savedRestaurant = r), Arg.Any<CancellationToken>());

        await _handler.Handle(new CreateRestaurantCommand("default_diner"), CancellationToken.None);

        savedRestaurant!.StaffRoster.Staff.Select(s => s.StaffId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_SeedsFloorLayout_FromLayoutRepository()
    {
        var config = BuildConfig();
        var layout = BuildLayout();
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(config);
        _layoutRepository.GetByIdAsync("default_diner_layout", Arg.Any<CancellationToken>()).Returns(layout);

        Restaurant? savedRestaurant = null;
        await _repository.SaveAsync(Arg.Do<Restaurant>(r => savedRestaurant = r), Arg.Any<CancellationToken>());

        await _handler.Handle(new CreateRestaurantCommand("default_diner"), CancellationToken.None);

        savedRestaurant!.Floor.Tables.Should().HaveCount(1);
        savedRestaurant.Floor.Clusters.Should().HaveCount(1);
        savedRestaurant.Floor.Freezers.Should().HaveCount(1);
        savedRestaurant.Floor.Bathrooms.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_SetsStartingCashAndReputation_FromConfig()
    {
        var config = BuildConfig();
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(config);
        _layoutRepository.GetByIdAsync("default_diner_layout", Arg.Any<CancellationToken>()).Returns(BuildLayout());

        Restaurant? savedRestaurant = null;
        await _repository.SaveAsync(Arg.Do<Restaurant>(r => savedRestaurant = r), Arg.Any<CancellationToken>());

        await _handler.Handle(new CreateRestaurantCommand("default_diner"), CancellationToken.None);

        savedRestaurant!.Cash.Should().Be(5000m);
        savedRestaurant.Reputation.Should().Be(20f);
        savedRestaurant.SimulationTick.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ReturnsDifferentGuids_ForTwoSeparateCreations()
    {
        var config = BuildConfig();
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(config);
        _layoutRepository.GetByIdAsync("default_diner_layout", Arg.Any<CancellationToken>()).Returns(_ => BuildLayout());

        var result1 = await _handler.Handle(new CreateRestaurantCommand("default_diner"), CancellationToken.None);
        var result2 = await _handler.Handle(new CreateRestaurantCommand("default_diner"), CancellationToken.None);

        result1.Value.Should().NotBe(result2.Value);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenConfigDoesNotExist()
    {
        _presetRepository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RestaurantConfig?)null);

        var result = await _handler.Handle(new CreateRestaurantCommand("nonexistent"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenLayoutDoesNotExist()
    {
        _presetRepository.GetAsync("default_diner", Arg.Any<CancellationToken>()).Returns(BuildConfig());
        _layoutRepository.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RestaurantLayout?)null);

        var result = await _handler.Handle(new CreateRestaurantCommand("default_diner"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static RestaurantConfig BuildConfig() => new()
    {
        ConfigId = "default_diner",
        StartingCash = 5000m,
        MinutesPerTick = 1,
        TicksPerRealSecond = 1,
        TableLayoutId = "default_diner_layout",
        StaffRosterSeed = new (StaffRole, int)[] { (StaffRole.Host, 1), (StaffRole.Waiter, 2) },
        MenuItemIds = Array.Empty<int>(),
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

    private static RestaurantLayout BuildLayout()
    {
        var shape = new TableShape { ShapeId = "standard_2top", SeatDefinitions = Array.Empty<SeatDefinition>(), CanMerge = false };
        var table = new Table
        {
            TableId = 1, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1),
            Shape = shape, Seats = Array.Empty<Seat>(), ClusterId = 1
        };
        var cluster = new TableCluster { ClusterId = 1, TableIds = new List<int> { 1 }, Capacity = 2 };
        var stationType = new KitchenStationType { StationTypeId = "grill", Capacity = 2, SpeedMultiplier = 1.0f };
        var station = new KitchenStation { StationId = 1, Position = new GridPosition(2, 0), Hitbox = new GridBounds(1, 1), StationType = stationType };
        var heatLamp = new HeatLamp { HeatLampId = 1, Position = new GridPosition(3, 0), Hitbox = new GridBounds(1, 1), SlotCount = 3, QualityDecayRatePerTick = 1f };
        var freezer = new Freezer { FreezerId = 1, Position = new GridPosition(4, 0), Hitbox = new GridBounds(1, 1) };
        var bathrooms = new List<Bathroom>
        {
            new() { BathroomId = 1, Gender = BathroomGender.Male, Position = new GridPosition(5, 0), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1 },
            new() { BathroomId = 2, Gender = BathroomGender.Female, Position = new GridPosition(5, 1), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1 },
            new() { BathroomId = 3, Gender = BathroomGender.Unisex, Position = new GridPosition(5, 2), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1 }
        };

        return new RestaurantLayout
        {
            Tables = new List<Table> { table },
            Clusters = new List<TableCluster> { cluster },
            KitchenStations = new List<KitchenStation> { station },
            HeatLamps = new List<HeatLamp> { heatLamp },
            Freezers = new List<Freezer> { freezer },
            Bathrooms = bathrooms
        };
    }
}
