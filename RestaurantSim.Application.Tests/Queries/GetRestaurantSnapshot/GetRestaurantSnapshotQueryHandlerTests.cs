using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Queries.GetRestaurantSnapshot;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Queries.GetRestaurantSnapshot;

public class GetRestaurantSnapshotQueryHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly GetRestaurantSnapshotQueryHandler _handler;

    public GetRestaurantSnapshotQueryHandlerTests()
    {
        _handler = new GetRestaurantSnapshotQueryHandler(_repository, _presetRepository);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new GetRestaurantSnapshotQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenConfigDoesNotExist()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns((RestaurantConfig?)null);

        var result = await _handler.Handle(new GetRestaurantSnapshotQuery(restaurant.RestaurantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MapsClockSettingsAndFloor()
    {
        var restaurant = BuildRestaurant();
        restaurant.SimulationTick = 42;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig(minutesPerTick: 10));

        var result = await _handler.Handle(new GetRestaurantSnapshotQuery(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var snapshot = result.Value!;
        snapshot.RestaurantId.Should().Be(restaurant.RestaurantId);
        snapshot.SimulationTick.Should().Be(42);
        snapshot.MinutesPerTick.Should().Be(10);
        snapshot.TicksPerRealSecond.Should().Be(1);

        var table = snapshot.Floor.Tables.Should().ContainSingle().Subject;
        table.Position.X.Should().Be(2);
        table.Position.Y.Should().Be(3);
        table.Size.Width.Should().Be(2);
        table.Size.Height.Should().Be(1);

        var cluster = snapshot.Floor.Clusters.Should().ContainSingle().Subject;
        cluster.State.Should().Be("Free");
        cluster.Capacity.Should().Be(2);
    }

    [Fact]
    public async Task Handle_MapsQueuedGroups()
    {
        var restaurant = BuildRestaurant();
        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = 5,
            PartySize = 3,
            Members = Array.Empty<Customer>(),
            ArrivalTick = 0
        });
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        var result = await _handler.Handle(new GetRestaurantSnapshotQuery(restaurant.RestaurantId), CancellationToken.None);

        var queued = result.Value!.Queue.Should().ContainSingle().Subject;
        queued.GroupId.Should().Be(5);
        queued.PartySize.Should().Be(3);
    }

    private static Restaurant BuildRestaurant()
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

        restaurant.Floor.Tables.Add(new Table
        {
            TableId = 1,
            Position = new GridPosition(2, 3),
            Hitbox = new GridBounds(2, 1),
            Shape = new TableShape
            {
                ShapeId = "standard_2top",
                SeatDefinitions = Array.Empty<SeatDefinition>(),
                CanMerge = true
            },
            Seats = Array.Empty<Seat>(),
            ClusterId = 1
        });

        restaurant.Floor.Clusters.Add(new TableCluster
        {
            ClusterId = 1,
            TableIds = new List<int> { 1 },
            Capacity = 2
        });

        return restaurant;
    }

    private static RestaurantConfig BuildConfig(int minutesPerTick = 1) => new()
    {
        ConfigId = "default_diner",
        StartingCash = 5000m,
        MinutesPerTick = minutesPerTick,
        TicksPerRealSecond = 1,
        TableLayoutId = "default_diner_layout",
        StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
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
}
