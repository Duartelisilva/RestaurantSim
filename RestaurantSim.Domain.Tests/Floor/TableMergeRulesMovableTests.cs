using System.Numerics;
using FluentAssertions;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Domain.Tests.Floor;

// Tables are movable: merging works across a small free gap and comes with a plan for who
// slides where. The touching-only behaviour is covered by TableMergeRulesTests.
public class TableMergeRulesMovableTests
{
    private static readonly TableShape NorthSouth = Shape(Orientation.South, Orientation.North);
    private static readonly TableShape EastWest = Shape(Orientation.East, Orientation.West);

    private static readonly IReadOnlyDictionary<int, int> NoLoad = new Dictionary<int, int>();

    private static IReadOnlySet<int> Free(params int[] ids) => ids.ToHashSet();

    private static MergeGeometry Gap(int maxGap, params GridPosition[] blocked) => new(maxGap, blocked.ToHashSet());

    // ---------- which tables can merge ----------

    [Fact]
    public void Lines_DefaultGeometry_TablesWithAGapDoNotMerge()
    {
        var (tables, _) = Row(0, 2, 4);

        TableMergeRules.MaxPartySize(tables).Should().Be(2);
    }

    [Fact]
    public void MaxPartySize_GapWithinTheLimit_ChainsTheWholeRow()
    {
        var (tables, _) = Row(0, 2, 4);

        TableMergeRules.MaxPartySize(tables, Gap(1)).Should().Be(6);
    }

    [Fact]
    public void MaxPartySize_GapBiggerThanTheLimit_BreaksTheChain()
    {
        var (tables, _) = Row(0, 3, 4); // 2 empty cells, then touching

        TableMergeRules.MaxPartySize(tables, Gap(1)).Should().Be(4);
    }

    [Fact]
    public void MaxPartySize_GapCellTakenBySomethingElse_BreaksTheChain()
    {
        var (tables, _) = Row(0, 2, 4);

        TableMergeRules.MaxPartySize(tables, Gap(1, new GridPosition(3, 0))).Should().Be(4);
    }

    [Fact]
    public void MaxPartySize_TablesInDifferentRows_NeverChain()
    {
        var tables = new[] { T(1, 0, 0), T(2, 1, 2) };

        TableMergeRules.MaxPartySize(tables, Gap(3)).Should().Be(2);
    }

    [Fact]
    public void IsValidMerge_AcrossAFreeGap_IsTrue_OnlyWithAGapGeometry()
    {
        var (tables, clusters) = Row(0, 2);

        TableMergeRules.IsValidMerge(new[] { 1, 2 }, tables, clusters, Free(1, 2)).Should().BeFalse();
        TableMergeRules.IsValidMerge(new[] { 1, 2 }, tables, clusters, Free(1, 2), Gap(1)).Should().BeTrue();
    }

    [Fact]
    public void FindMerge_AcrossAGap_PicksTheSmallestWindowThatFits()
    {
        var (tables, clusters) = Row(0, 2, 4);

        var plan = TableMergeRules.FindMerge(3, tables, clusters, Free(1, 2, 3), Gap(1));

        plan.Should().NotBeNull();
        plan!.ClusterIds.Should().Equal(1, 2);
        plan.Capacity.Should().Be(4);
    }

    [Fact]
    public void MergeWindows_VerticalLane_WorkTheSameWay()
    {
        var tables = new[] { T(1, 0, 0, EastWest), T(2, 0, 2, EastWest) };
        var clusters = new[] { C(1, 1), C(2, 2) };

        TableMergeRules.IsValidMerge(new[] { 1, 2 }, tables, clusters, Free(1, 2), Gap(1)).Should().BeTrue();
    }

    // ---------- who moves: three or more ----------

    [Fact]
    public void PlanMerge_ThreeInARow_TheMiddleStaysAndTheOuterOnesSlideIn()
    {
        var (tables, clusters) = Row(0, 2, 4);

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2, 3 }, tables, clusters, NoLoad, Gap(1))!;

        plan.StayingClusterId.Should().Be(2);
        plan.Moves.Should().Equal(
            new TableMove(1, new GridPosition(0, 0), new GridPosition(1, 0)),
            new TableMove(3, new GridPosition(4, 0), new GridPosition(3, 0)));
    }

    [Fact]
    public void PlanMerge_FourInARow_TheLowerMiddleStays()
    {
        var (tables, clusters) = Row(0, 2, 4, 6);

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2, 3, 4 }, tables, clusters, NoLoad, Gap(1))!;

        plan.StayingClusterId.Should().Be(2);
        plan.Moves.Select(m => (m.TableId, m.To.X)).Should().Equal((1, 1), (3, 3), (4, 4));
    }

    [Fact]
    public void PlanMerge_ClusterOfTwoTables_SlidesAsOneRigidBlock()
    {
        // T1 | gap | T2 T3 (one cluster, touching) | gap | T4
        var tables = new[] { T(1, 0, 0), T(2, 2, 0, clusterId: 2), T(3, 3, 0, clusterId: 2), T(4, 5, 0) };
        var clusters = new[] { C(1, 1), C(2, 2, 3), C(4, 4) };

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2, 4 }, tables, clusters, NoLoad, Gap(1))!;

        plan.StayingClusterId.Should().Be(2);
        plan.Moves.Select(m => (m.TableId, m.To.X)).Should().Equal((1, 1), (4, 4));
    }

    [Fact]
    public void PlanMerge_AlreadyTouching_NeedsNoMoves()
    {
        var (tables, clusters) = Row(0, 1, 2);

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2, 3 }, tables, clusters, NoLoad, Gap(1))!;

        plan.Moves.Should().BeEmpty();
    }

    // ---------- who moves: two ----------

    [Fact]
    public void PlanMerge_Two_TheTableOfTheLessBusyWaiterStaysAndTheOtherSlidesToIt()
    {
        var (tables, clusters) = Row(0, 2);
        clusters[0].AssignedWaiterId = 10;
        clusters[1].AssignedWaiterId = 20;
        var load = new Dictionary<int, int> { [10] = 3, [20] = 1 };

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2 }, tables, clusters, load, Gap(1))!;

        plan.StayingClusterId.Should().Be(2);
        plan.WaiterId.Should().Be(20);
        plan.Moves.Should().Equal(new TableMove(1, new GridPosition(0, 0), new GridPosition(1, 0)));
    }

    [Fact]
    public void PlanMerge_Two_EqualLoad_LowestWaiterIdWins()
    {
        var (tables, clusters) = Row(0, 2);
        clusters[0].AssignedWaiterId = 20;
        clusters[1].AssignedWaiterId = 10;

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2 }, tables, clusters, NoLoad, Gap(1))!;

        plan.WaiterId.Should().Be(10);
        plan.StayingClusterId.Should().Be(2);
    }

    [Fact]
    public void PlanMerge_Two_SameWaiter_TheLowestTableIdStays()
    {
        var (tables, clusters) = Row(0, 2);
        clusters[0].AssignedWaiterId = 10;
        clusters[1].AssignedWaiterId = 10;

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2 }, tables, clusters, NoLoad, Gap(1))!;

        plan.WaiterId.Should().Be(10);
        plan.StayingClusterId.Should().Be(1);
        plan.Moves.Single().TableId.Should().Be(2);
    }

    [Fact]
    public void PlanMerge_Two_OnlyOneClusterHasAWaiter_ThatClusterStays()
    {
        var (tables, clusters) = Row(0, 2);
        clusters[1].AssignedWaiterId = 10;

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2 }, tables, clusters, NoLoad, Gap(1))!;

        plan.WaiterId.Should().Be(10);
        plan.StayingClusterId.Should().Be(2);
    }

    [Fact]
    public void PlanMerge_Two_NoWaiters_LowestTableIdStays_AndNoWaiterIsChosen()
    {
        var (tables, clusters) = Row(0, 2);

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2 }, tables, clusters, NoLoad, Gap(1))!;

        plan.WaiterId.Should().BeNull();
        plan.StayingClusterId.Should().Be(1);
    }

    [Fact]
    public void PlanMerge_Three_ChoosesTheWaiterFromTheMergedClustersOnly()
    {
        var (tables, clusters) = Row(0, 2, 4);
        clusters[0].AssignedWaiterId = 10;
        clusters[2].AssignedWaiterId = 20;
        var load = new Dictionary<int, int> { [10] = 2, [20] = 0, [99] = 0 }; // 99 is not an owner of these clusters

        var plan = TableMergeRules.PlanMerge(new[] { 1, 2, 3 }, tables, clusters, load, Gap(1))!;

        plan.WaiterId.Should().Be(20);
        plan.StayingClusterId.Should().Be(2); // the middle one still stays
    }

    // ---------- plans that must not exist ----------

    [Fact]
    public void PlanMerge_ClustersNotNextToEachOther_IsNull()
    {
        var (tables, clusters) = Row(0, 2, 4);

        TableMergeRules.PlanMerge(new[] { 1, 3 }, tables, clusters, NoLoad, Gap(1)).Should().BeNull();
    }

    [Fact]
    public void PlanMerge_GapTooBig_IsNull()
    {
        var (tables, clusters) = Row(0, 3);

        TableMergeRules.PlanMerge(new[] { 1, 2 }, tables, clusters, NoLoad, Gap(1)).Should().BeNull();
    }

    [Fact]
    public void PlanMerge_SingleClusterOrRepeatedIds_IsNull()
    {
        var (tables, clusters) = Row(0, 2);

        TableMergeRules.PlanMerge(new[] { 1 }, tables, clusters, NoLoad, Gap(1)).Should().BeNull();
        TableMergeRules.PlanMerge(new[] { 1, 1 }, tables, clusters, NoLoad, Gap(1)).Should().BeNull();
    }

    [Fact]
    public void PlanMerge_UnknownCluster_IsNull()
    {
        var (tables, clusters) = Row(0, 2);

        TableMergeRules.PlanMerge(new[] { 1, 9 }, tables, clusters, NoLoad, Gap(1)).Should().BeNull();
    }

    // ---------- going back home ----------

    [Fact]
    public void PlanReturn_MovedTables_GoBackToTheirHomePosition()
    {
        var (tables, _) = Row(0, 2, 4);
        tables[0].Position = new GridPosition(1, 0);
        tables[2].Position = new GridPosition(3, 0);

        var moves = TableMergeRules.PlanReturn(tables, new HashSet<GridPosition>())!;

        moves.Should().Equal(
            new TableMove(1, new GridPosition(1, 0), new GridPosition(0, 0)),
            new TableMove(3, new GridPosition(3, 0), new GridPosition(4, 0)));
    }

    [Fact]
    public void PlanReturn_TablesAlreadyHome_HasNothingToDo()
    {
        var (tables, _) = Row(0, 2);

        TableMergeRules.PlanReturn(tables, new HashSet<GridPosition>()).Should().BeEmpty();
    }

    [Fact]
    public void PlanReturn_AHomeCellIsTaken_IsNull_SoNothingMovesHalfway()
    {
        var (tables, _) = Row(0, 2, 4);
        tables[0].Position = new GridPosition(1, 0);
        tables[2].Position = new GridPosition(3, 0);

        TableMergeRules.PlanReturn(tables, new HashSet<GridPosition> { new(4, 0) }).Should().BeNull();
    }

    // ---------- blocked cells of a real floor ----------

    [Fact]
    public void MergeGeometry_For_BlocksEveryPlaceableCell_AndCanIgnoreSomeTables()
    {
        var floor = new FloorState();
        floor.Tables.Add(T(1, 0, 0));
        floor.Tables.Add(T(2, 2, 0));
        floor.Freezers.Add(new Freezer { FreezerId = 1, Position = new GridPosition(5, 5), Hitbox = new GridBounds(1, 1) });

        MergeGeometry.For(floor, 1).BlockedCells.Should().BeEquivalentTo(
            new[] { new GridPosition(0, 0), new GridPosition(2, 0), new GridPosition(5, 5) });

        MergeGeometry.BlockedCellsOf(floor, new[] { 2 }).Should().BeEquivalentTo(
            new[] { new GridPosition(0, 0), new GridPosition(5, 5) });
    }

    // ---------- helpers ----------

    // One standalone 2-top per x, table n in cluster n, all in row 0.
    private static (IReadOnlyList<Table> Tables, IReadOnlyList<TableCluster> Clusters) Row(params int[] xs)
    {
        var tables = xs.Select((x, i) => T(i + 1, x, 0)).ToList();
        var clusters = xs.Select((_, i) => C(i + 1, i + 1)).ToList();
        return (tables, clusters);
    }

    private static Table T(int id, int x, int y, TableShape? shape = null, int? clusterId = null)
    {
        var s = shape ?? NorthSouth;
        return new Table
        {
            TableId = id,
            Position = new GridPosition(x, y),
            Hitbox = new GridBounds(1, 1),
            Shape = s,
            Seats = s.SeatDefinitions.Select(d => new Seat
            {
                SeatIndex = d.SeatIndex, RelativeOffset = d.RelativeOffset, Orientation = d.Orientation
            }).ToList(),
            ClusterId = clusterId ?? id
        };
    }

    private static TableCluster C(int id, params int[] tableIds) => new()
    {
        ClusterId = id, TableIds = tableIds.ToList(), Capacity = tableIds.Length * 2
    };

    private static TableShape Shape(Orientation first, Orientation second) => new()
    {
        ShapeId = "test_2top",
        CanMerge = true,
        SeatDefinitions = new List<SeatDefinition>
        {
            new(0, new Vector2(0, -0.5f), first),
            new(1, new Vector2(0, 0.5f), second)
        }
    };
}
