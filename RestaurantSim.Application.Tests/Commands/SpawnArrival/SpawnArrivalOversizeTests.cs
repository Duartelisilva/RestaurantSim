using System.Numerics;
using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.SpawnArrival;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SpawnArrival;

// ArrivalGroupGenerator rolls the party size from the first NextDouble():
// 0.10 -> a party of 1, 0.99 -> a party of 6.
public class SpawnArrivalOversizeTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IRandomProvider _random = Substitute.For<IRandomProvider>();
    private readonly SpawnArrivalCommandHandler _handler;

    public SpawnArrivalOversizeTests()
    {
        _handler = new SpawnArrivalCommandHandler(_repository, _presetRepository, _publisher, new ArrivalGroupGenerator(_random));
    }

    [Fact]
    public async Task APartyLargerThanTheLayoutCanEverSeat_IsTurnedAwayAtTheDoorAsync()
    {
        _random.NextDouble().Returns(0.99); // party of 6
        var restaurant = BuildRestaurant(tablesInARow: 1);          // the layout seats at most 2

        var result = await RunAsync(restaurant, penalty: 0f);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.Queue.Should().BeEmpty();
        restaurant.Reputation.Should().Be(50f);                      // default penalty is 0
        restaurant.CustomerService.NextArrivalTick.Should().BeGreaterThan(restaurant.SimulationTick);
        await _publisher.Received(1).Publish(
            Arg.Is<GroupTurnedAway>(e => e.Reason == TurnedAwayReason.PartyTooLarge && e.ReputationAfter == 50f),
            Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<GroupArrived>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOversizePenalty_IsAConfiguredFractionOfCurrentReputationAsync()
    {
        _random.NextDouble().Returns(0.99);
        var restaurant = BuildRestaurant(tablesInARow: 1);

        await RunAsync(restaurant, penalty: 0.1f);

        restaurant.Reputation.Should().BeApproximately(45f, 0.001f);
        await _publisher.Received(1).Publish(
            Arg.Is<GroupTurnedAway>(e => e.ReputationAfter > 44.9f && e.ReputationAfter < 45.1f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APartyThatMergedTablesCouldSeat_JoinsTheQueueAsync()
    {
        _random.NextDouble().Returns(0.99); // party of 6
        var restaurant = BuildRestaurant(tablesInARow: 3);          // three touching 2-tops = 6

        await RunAsync(restaurant, penalty: 0.5f);

        restaurant.CustomerService.Queue.Should().ContainSingle().Which.PartySize.Should().Be(6);
        restaurant.Reputation.Should().Be(50f);
    }

    [Fact]
    public async Task APartyThatFitsOneTable_JoinsTheQueueAsync()
    {
        _random.NextDouble().Returns(0.10); // party of 1
        var restaurant = BuildRestaurant(tablesInARow: 1);

        await RunAsync(restaurant, penalty: 0.5f);

        restaurant.CustomerService.Queue.Should().ContainSingle();
    }

    [Fact]
    public async Task ALayoutWithNoTablesAtAll_AppliesNoSizeLimitAsync()
    {
        _random.NextDouble().Returns(0.99);
        var restaurant = BuildRestaurant(tablesInARow: 0);

        await RunAsync(restaurant, penalty: 0.5f);

        restaurant.CustomerService.Queue.Should().ContainSingle();
    }

    private async Task<RestaurantSim.Application.Common.Result> RunAsync(Restaurant restaurant, float penalty)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig(penalty));
        return await _handler.Handle(new SpawnArrivalCommand(restaurant.RestaurantId), CancellationToken.None);
    }

    private static Restaurant BuildRestaurant(int tablesInARow)
    {
        var shape = new TableShape
        {
            ShapeId = "standard_2top",
            CanMerge = true,
            SeatDefinitions = new List<SeatDefinition>
            {
                new(0, new Vector2(0, -0.5f), Orientation.South),
                new(1, new Vector2(0, 0.5f), Orientation.North)
            }
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            SimulationTick = 750, // 12:30, inside lunch
            Reputation = 50f,
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState { NextArrivalTick = 750 },
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        for (var id = 1; id <= tablesInARow; id++)
        {
            restaurant.Floor.Tables.Add(new Table
            {
                TableId = id,
                Position = new GridPosition(id - 1, 0),
                Hitbox = new GridBounds(1, 1),
                Shape = shape,
                Seats = shape.SeatDefinitions.Select(d => new Seat { SeatIndex = d.SeatIndex, RelativeOffset = d.RelativeOffset, Orientation = d.Orientation }).ToList(),
                ClusterId = id
            });
        }

        return restaurant;
    }

    private static RestaurantConfig BuildConfig(float oversizePenalty) => new()
    {
        ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
        TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
        MenuItemIds = Array.Empty<int>(), ReputationStart = 20f,
        QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f,
        ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
        ActiveMealPeriods = new List<MealPeriodDefinition>
        {
            new() { MealPeriodId = "lunch", StartTime = new TimeOfDay(12, 0), EndTime = new TimeOfDay(15, 0) }
        },
        EventModuleIds = Array.Empty<string>(), TipBaseRate = 50f, TipMoodExponent = 0.75f,
        MaxQueueLength = 12, BaseArrivalIntervalSeconds = 15f,
        OversizedPartyReputationPenalty = oversizePenalty
    };
}
