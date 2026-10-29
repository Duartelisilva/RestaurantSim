using System.Numerics;
using FluentAssertions;
using RestaurantSim.AutoManager.Strategies.TableLayout;
using RestaurantSim.Domain;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.AutoManager.Tests.Strategies.TableLayout;

public class DefaultTableLayoutStrategyTests
{
    private readonly DefaultTableLayoutStrategy _strategy = new();

    [Fact]
    public void NothingWaitingAndNothingMerged_DoesNothing()
    {
        var restaurant = BuildRestaurant(tableCount: 3);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AGroupThatFitsAFreeTable_GetsSeatedFirst_NoMerge()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        Queue(restaurant, groupId: 1, partySize: 2);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AGroupNoSingleTableFits_GetsTheSmallestMergeThatIsBigEnough()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        Queue(restaurant, groupId: 1, partySize: 4);

        var decision = _strategy.DecideNextLayoutChange(restaurant);

        decision.Should().BeOfType<MergeClustersDecision>()
            .Which.ClusterIds.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void AGroupOfSix_NeedsAllThreeTablesOfTheRow()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        Queue(restaurant, groupId: 1, partySize: 6);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeOfType<MergeClustersDecision>()
            .Which.ClusterIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void TablesThatDoNotTouch_CannotBeMergedForTheGroup()
    {
        var restaurant = BuildRestaurant(tableCount: 3, spacing: 2);
        Queue(restaurant, groupId: 1, partySize: 4);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void WhenTheFirstGroupCannotBeMerged_TheNextOneStillIs()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        Queue(restaurant, groupId: 1, partySize: 8);   // nothing can ever seat this many
        Queue(restaurant, groupId: 2, partySize: 4);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeOfType<MergeClustersDecision>();
    }

    [Fact]
    public void AMergedGroupNobodyNeeds_IsSplitBackToNormal()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        MergeByHand(restaurant, 1, 2);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeOfType<SplitClusterDecision>()
            .Which.ClusterId.Should().Be(1);
    }

    [Fact]
    public void AMergedGroupAWaitingPartyNeeds_IsKept_SoThatPartyGetsIt()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        MergeByHand(restaurant, 1, 2);
        Queue(restaurant, groupId: 1, partySize: 3);   // too big for a 2-top, fits the merged 4

        // Not split; and since the party fits the merged table, seating goes next.
        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AMergedGroupIsSplit_WhenTheOnlyWaitingPartyFitsASingleTable()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        MergeByHand(restaurant, 1, 2);
        Queue(restaurant, groupId: 1, partySize: 2);   // does not need the merge

        _strategy.DecideNextLayoutChange(restaurant).Should().BeOfType<SplitClusterDecision>();
    }

    [Fact]
    public void AMergedGroupThatIsStillInUse_IsLeftAlone()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        MergeByHand(restaurant, 1, 2);
        restaurant.Floor.Clusters.Single(c => c.ClusterId == 1).OccupiedBy = 9;

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AMergedGroupWithATableStillDirty_IsLeftAloneUntilItIsClean()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        MergeByHand(restaurant, 1, 2);
        restaurant.Floor.Tables.First(t => t.TableId == 2).IsClean = false;

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    [Fact]
    public void AClusterAuthoredPreMerged_IsNeverSplit()
    {
        var restaurant = BuildRestaurant(tableCount: 2);
        var authored = restaurant.Floor.Clusters.Single(c => c.ClusterId == 1);
        authored.TableIds = new List<int> { 1, 2 };   // MergedFrom stays null: that IS its normal state
        authored.Capacity = 4;
        restaurant.Floor.Clusters.RemoveAll(c => c.ClusterId == 2);

        _strategy.DecideNextLayoutChange(restaurant).Should().BeNull();
    }

    // ---------- helpers ----------

    // `tableCount` standalone 2-tops in a row, table n in cluster n; `spacing` 1 = touching.
    private static Restaurant BuildRestaurant(int tableCount, int spacing = 1)
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
            SimulationTick = 100,
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        for (var id = 1; id <= tableCount; id++)
        {
            restaurant.Floor.Tables.Add(new Table
            {
                TableId = id,
                Position = new GridPosition((id - 1) * spacing, 0),
                Hitbox = new GridBounds(1, 1),
                Shape = shape,
                Seats = shape.SeatDefinitions.Select(d => new Seat { SeatIndex = d.SeatIndex, RelativeOffset = d.RelativeOffset, Orientation = d.Orientation }).ToList(),
                ClusterId = id
            });

            restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = id, TableIds = new List<int> { id }, Capacity = 2 });
        }

        return restaurant;
    }

    private static void Queue(Restaurant restaurant, int groupId, int partySize)
    {
        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = groupId,
            PartySize = partySize,
            Members = new List<Customer>(),
            ArrivalTick = 0
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
