using System.Numerics;
using FluentAssertions;
using RestaurantSim.AutoManager.Strategies.TableLayout;
using RestaurantSim.Domain;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.AutoManager.Tests.Strategies.TableLayout;

// Tables are pushed together across a small gap, and a table already on its way counts.
public class DefaultTableLayoutStrategyMovableTests
{
    private readonly DefaultTableLayoutStrategy _strategy = new();

    [Fact]
    public void TablesAGapApart_CanBeMergedForABigParty_WhenTheFloorAllowsTheGap()
    {
        var restaurant = Row(maxGap: 1, 0, 2, 4);
        Queue(restaurant, partySize: 4);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeOfType<MergeClustersDecision>()
            .Which.ClusterIds.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void TablesAGapApart_CannotBeMerged_WhenTheFloorRequiresTouching()
    {
        var restaurant = Row(maxGap: 0, 0, 2, 4);
        Queue(restaurant, partySize: 4);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AMergeThatIsAlreadySlidingIntoPlaceForTheParty_IsWaitedFor_NotRepeatedElsewhere()
    {
        // Clusters 1+2 are being pushed together (table 2 is still sliding); 3 and 4 could
        // also merge, but the party of 4 will sit at 1+2 in a moment.
        var restaurant = Row(maxGap: 1, 0, 2, 4, 6);
        MergeByHand(restaurant, 1, 2);
        restaurant.Floor.Tables.Single(t => t.TableId == 2).StartMove(new GridPosition(1, 0), endTick: 105);
        Queue(restaurant, partySize: 4);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AMergedClusterThatIsStillMoving_IsNotSplitBackYet()
    {
        var restaurant = Row(maxGap: 1, 0, 2);
        MergeByHand(restaurant, 1, 2);
        restaurant.Floor.Tables.Single(t => t.TableId == 2).StartMove(new GridPosition(1, 0), endTick: 105);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AMergedClusterThatHasArrivedAndIsNotNeeded_IsSplitBack()
    {
        var restaurant = Row(maxGap: 1, 0, 1);
        MergeByHand(restaurant, 1, 2);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeOfType<SplitClusterDecision>()
            .Which.ClusterId.Should().Be(1);
    }

    // ---------- helpers ----------

    private static Restaurant Row(int maxGap, params int[] xs)
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
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = 100,
            Floor = new FloorState { MaxMergeGapCells = maxGap }, StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };

        for (var i = 0; i < xs.Length; i++)
        {
            var id = i + 1;
            restaurant.Floor.Tables.Add(new Table
            {
                TableId = id, Position = new GridPosition(xs[i], 0), Hitbox = new GridBounds(1, 1), Shape = shape,
                Seats = shape.SeatDefinitions.Select(d => new Seat { SeatIndex = d.SeatIndex, RelativeOffset = d.RelativeOffset, Orientation = d.Orientation }).ToList(),
                ClusterId = id
            });
            restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = id, TableIds = new List<int> { id }, Capacity = 2 });
        }

        return restaurant;
    }

    private static void Queue(Restaurant restaurant, int partySize)
    {
        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = 1, PartySize = partySize, Members = new List<Customer>(), ArrivalTick = 0
        });
    }

    // What MergeClustersCommand leaves behind, built directly for the strategy tests.
    private static void MergeByHand(Restaurant restaurant, int firstClusterId, int secondClusterId)
    {
        var first = restaurant.Floor.Clusters.Single(c => c.ClusterId == firstClusterId);
        var second = restaurant.Floor.Clusters.Single(c => c.ClusterId == secondClusterId);

        first.MergedFrom = new List<ClusterComposition>
        {
            new(first.ClusterId, first.TableIds.ToList(), first.Capacity, null, false),
            new(second.ClusterId, second.TableIds.ToList(), second.Capacity, null, false)
        };
        first.TableIds = first.TableIds.Concat(second.TableIds).ToList();
        first.Capacity += second.Capacity;

        foreach (var table in restaurant.Floor.Tables.Where(t => first.TableIds.Contains(t.TableId)))
            table.ClusterId = first.ClusterId;

        restaurant.Floor.Clusters.Remove(second);
    }
}
