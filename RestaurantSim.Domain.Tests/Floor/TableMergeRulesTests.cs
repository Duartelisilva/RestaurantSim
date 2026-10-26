using System.Numerics;
using FluentAssertions;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Domain.Tests.Floor;

public class TableMergeRulesTests
{
    // Seats face North/South, so these tables merge left-right (like the default 2-top).
    private static readonly TableShape NorthSouth = Shape(
        canMerge: true, Orientation.South, Orientation.North);

    private static readonly TableShape EastWest = Shape(
        canMerge: true, Orientation.East, Orientation.West);

    private static readonly TableShape Round = Shape(
        canMerge: false, Orientation.South, Orientation.North);

    private static IReadOnlySet<int> Free(params int[] ids) => ids.ToHashSet();

    // ---------- axis ----------

    [Fact]
    public void AxisOf_SeatsFacingNorthSouth_MergeLeftRight()
    {
        TableMergeRules.AxisOf(T(1, 0, 0)).Should().Be(MergeAxis.Horizontal);
    }

    [Fact]
    public void AxisOf_SeatsFacingEastWest_MergeTopBottom()
    {
        TableMergeRules.AxisOf(T(1, 0, 0, EastWest)).Should().Be(MergeAxis.Vertical);
    }

    [Fact]
    public void AxisOf_ShapeThatCannotMerge_IsNull()
    {
        TableMergeRules.AxisOf(T(1, 0, 0, Round)).Should().BeNull();
    }

    // ---------- the largest party the layout could ever seat ----------

    [Fact]
    public void MaxPartySize_TablesWithGapsBetweenThem_IsTheBiggestTouchingRun()
    {
        // The default layout: T1 (0,0) T2 (2,0) T3 (0,2) T4 (2,2) T5 (4,0) T6 (5,0).
        var tables = new[] { T(1, 0, 0), T(2, 2, 0), T(3, 0, 2), T(4, 2, 2), T(5, 4, 0), T(6, 5, 0) };

        TableMergeRules.MaxPartySize(tables).Should().Be(4);
    }

    [Fact]
    public void MaxPartySize_ARowOfThreeTouchingTables_IsSix()
    {
        var tables = new[] { T(1, 0, 0), T(2, 1, 0), T(3, 2, 0) };

        TableMergeRules.MaxPartySize(tables).Should().Be(6);
    }

    [Fact]
    public void MaxPartySize_TouchingTablesThatCannotMerge_DoNotAddUp()
    {
        var tables = new[] { T(1, 0, 0, Round), T(2, 1, 0, Round) };

        TableMergeRules.MaxPartySize(tables).Should().Be(2);
    }

    [Fact]
    public void MaxPartySize_TablesInDifferentRows_DoNotAddUp()
    {
        var tables = new[] { T(1, 0, 0), T(2, 1, 1) };

        TableMergeRules.MaxPartySize(tables).Should().Be(2);
    }

    [Fact]
    public void MaxPartySize_VerticalLine_UsesTheVerticalAxis()
    {
        var tables = new[] { T(1, 0, 0, EastWest), T(2, 0, 1, EastWest), T(3, 0, 2, EastWest) };

        TableMergeRules.MaxPartySize(tables).Should().Be(6);
    }

    [Fact]
    public void MaxPartySize_NoTables_IsZero()
    {
        TableMergeRules.MaxPartySize(Array.Empty<Table>()).Should().Be(0);
    }

    // ---------- finding a merge ----------

    [Fact]
    public void FindMerge_PicksTheSmallestMergeThatFits()
    {
        var (tables, clusters) = Row(count: 3);

        var plan = TableMergeRules.FindMerge(3, tables, clusters, Free(1, 2, 3));

        plan.Should().NotBeNull();
        plan!.Capacity.Should().Be(4);          // two 2-tops are enough for 3 people
        plan.ClusterIds.Should().HaveCount(2);
    }

    [Fact]
    public void FindMerge_PartyOfSix_NeedsAllThreeInTheRow()
    {
        var (tables, clusters) = Row(count: 3);

        var plan = TableMergeRules.FindMerge(6, tables, clusters, Free(1, 2, 3));

        plan!.ClusterIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        plan.Capacity.Should().Be(6);
    }

    [Fact]
    public void FindMerge_AnOccupiedClusterInTheMiddleBreaksTheLine()
    {
        var (tables, clusters) = Row(count: 3);

        // Cluster 2 is not free: 1 and 3 are separated by it and cannot join.
        TableMergeRules.FindMerge(4, tables, clusters, Free(1, 3)).Should().BeNull();
    }

    [Fact]
    public void FindMerge_ClustersThatDoNotTouch_CannotMerge()
    {
        var tables = new[] { T(1, 0, 0, clusterId: 1), T(2, 2, 0, clusterId: 2) };
        var clusters = new[] { C(1, 1), C(2, 2) };

        TableMergeRules.FindMerge(4, tables, clusters, Free(1, 2)).Should().BeNull();
    }

    [Fact]
    public void FindMerge_AnAlreadyMergedClusterCanJoinAnAdjacentTable()
    {
        // The authored pre-merged T5+T6 (cluster 5, capacity 4) next to a free T7.
        var tables = new[] { T(5, 4, 0, clusterId: 5), T(6, 5, 0, clusterId: 5), T(7, 6, 0, clusterId: 7) };
        var clusters = new[] { C(5, 5, 6), C(7, 7) };

        var plan = TableMergeRules.FindMerge(6, tables, clusters, Free(5, 7));

        plan!.ClusterIds.Should().BeEquivalentTo(new[] { 5, 7 });
        plan.Capacity.Should().Be(6);
    }

    [Fact]
    public void FindMerge_IsDeterministic_WhenSeveralMergesFit()
    {
        // Two separate pairs of the same size: the one with the lowest cluster id wins.
        var tables = new[]
        {
            T(1, 0, 0, clusterId: 1), T(2, 1, 0, clusterId: 2),
            T(3, 0, 5, clusterId: 3), T(4, 1, 5, clusterId: 4)
        };
        var clusters = new[] { C(1, 1), C(2, 2), C(3, 3), C(4, 4) };

        TableMergeRules.FindMerge(4, tables, clusters, Free(1, 2, 3, 4))!
            .ClusterIds.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void FindMerge_PartyTooBigForAnyLine_IsNull()
    {
        var (tables, clusters) = Row(count: 2);

        TableMergeRules.FindMerge(5, tables, clusters, Free(1, 2)).Should().BeNull();
    }

    // ---------- validating a requested merge ----------

    [Fact]
    public void IsValidMerge_AdjacentFreeClusters_IsTrue()
    {
        var (tables, clusters) = Row(count: 3);

        TableMergeRules.IsValidMerge(new[] { 2, 3 }, tables, clusters, Free(1, 2, 3)).Should().BeTrue();
        TableMergeRules.IsValidMerge(new[] { 1, 2, 3 }, tables, clusters, Free(1, 2, 3)).Should().BeTrue();
    }

    [Fact]
    public void IsValidMerge_SkippingAClusterInTheMiddle_IsFalse()
    {
        var (tables, clusters) = Row(count: 3);

        TableMergeRules.IsValidMerge(new[] { 1, 3 }, tables, clusters, Free(1, 2, 3)).Should().BeFalse();
    }

    [Fact]
    public void IsValidMerge_AClusterThatIsNotFree_IsFalse()
    {
        var (tables, clusters) = Row(count: 2);

        TableMergeRules.IsValidMerge(new[] { 1, 2 }, tables, clusters, Free(1)).Should().BeFalse();
    }

    [Fact]
    public void IsValidMerge_ASingleCluster_IsFalse()
    {
        var (tables, clusters) = Row(count: 2);

        TableMergeRules.IsValidMerge(new[] { 1 }, tables, clusters, Free(1, 2)).Should().BeFalse();
    }

    [Fact]
    public void IsValidMerge_RepeatedIds_IsFalse()
    {
        var (tables, clusters) = Row(count: 2);

        TableMergeRules.IsValidMerge(new[] { 1, 1 }, tables, clusters, Free(1, 2)).Should().BeFalse();
    }

    // ---------- helpers ----------

    // `count` standalone 2-tops side by side, table n in cluster n.
    private static (IReadOnlyList<Table> Tables, IReadOnlyList<TableCluster> Clusters) Row(int count)
    {
        var tables = Enumerable.Range(1, count).Select(i => T(i, i - 1, 0, clusterId: i)).ToList();
        var clusters = Enumerable.Range(1, count).Select(i => C(i, i)).ToList();
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
                SeatIndex = d.SeatIndex,
                RelativeOffset = d.RelativeOffset,
                Orientation = d.Orientation
            }).ToList(),
            ClusterId = clusterId ?? id
        };
    }

    private static TableCluster C(int id, params int[] tableIds) => new()
    {
        ClusterId = id,
        TableIds = tableIds.ToList(),
        Capacity = tableIds.Length * 2
    };

    private static TableShape Shape(bool canMerge, Orientation first, Orientation second) => new()
    {
        ShapeId = "test_2top",
        CanMerge = canMerge,
        SeatDefinitions = new List<SeatDefinition>
        {
            new(0, new Vector2(0, -0.5f), first),
            new(1, new Vector2(0, 0.5f), second)
        }
    };
}
