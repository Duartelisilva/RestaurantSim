using FluentAssertions;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Infrastructure.Presets;

namespace RestaurantSim.Infrastructure.Tests.Presets;

// The preset is data, but a typo in it (two things on one cell, a table placed out of its
// lane) silently breaks merging, so the shape the design docs promise is pinned here.
public class DefaultDinerLayoutSeedTests
{
    [Fact]
    public void SixStandaloneTwoTops_EachInItsOwnCluster()
    {
        var layout = DefaultDinerLayoutSeed.Create();

        layout.Tables.Select(t => t.TableId).Should().Equal(1, 2, 3, 4, 5, 6);
        layout.Clusters.Select(c => c.ClusterId).Should().Equal(1, 2, 3, 4, 5, 6);
        layout.Clusters.Should().OnlyContain(c => c.Capacity == 2 && c.TableIds.Count == 1);
        layout.Clusters.Select(c => c.TableIds.Single()).Should().Equal(1, 2, 3, 4, 5, 6);
        layout.Tables.Should().OnlyContain(t => t.ClusterId == t.TableId);
    }

    [Fact]
    public void TheTablesSitInTwoRowsOfThree_WithAnEmptyCellBetweenNeighbours()
    {
        var positions = DefaultDinerLayoutSeed.Create().Tables.ToDictionary(t => t.TableId, t => (t.Position.X, t.Position.Y));

        positions[1].Should().Be((0, 0));
        positions[2].Should().Be((2, 0));
        positions[5].Should().Be((4, 0));
        positions[3].Should().Be((0, 2));
        positions[4].Should().Be((2, 2));
        positions[6].Should().Be((4, 2));
    }

    [Fact]
    public void TablesStartAtHome()
    {
        DefaultDinerLayoutSeed.Create().Tables.Should().OnlyContain(t => t.Position == t.HomePosition && !t.IsMoving);
    }

    [Fact]
    public void NoTwoThingsShareACell()
    {
        var layout = DefaultDinerLayoutSeed.Create();
        var placeables = new List<IPlaceable>();
        placeables.AddRange(layout.Tables);
        placeables.AddRange(layout.KitchenStations);
        placeables.AddRange(layout.HeatLamps);
        placeables.AddRange(layout.Freezers);
        placeables.AddRange(layout.Bathrooms);

        var cells = placeables.SelectMany(Footprint.Cells).ToList();

        cells.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void WithTheGapOfOne_ARowMergesIntoSix_SoThePartyLimitIsSix()
    {
        var floor = Floor(DefaultDinerLayoutSeed.Create());

        TableMergeRules.MaxPartySize(floor.Tables, MergeGeometry.For(floor, maxGapCells: 1)).Should().Be(6);
        TableMergeRules.MaxPartySize(floor.Tables, MergeGeometry.For(floor, maxGapCells: 0)).Should().Be(2);
    }

    [Theory]
    [InlineData(new[] { 1, 2 })]
    [InlineData(new[] { 3, 4 })]
    [InlineData(new[] { 2, 5 })]
    [InlineData(new[] { 4, 6 })]
    [InlineData(new[] { 1, 2, 5 })]
    [InlineData(new[] { 3, 4, 6 })]
    public void RowMates_CanBePushedTogether(int[] clusterIds)
    {
        var floor = Floor(DefaultDinerLayoutSeed.Create());
        var free = floor.Clusters.Select(c => c.ClusterId).ToHashSet();

        TableMergeRules.IsValidMerge(clusterIds, floor.Tables, floor.Clusters, free, MergeGeometry.For(floor, 1)).Should().BeTrue();
    }

    [Theory]
    [InlineData(new[] { 1, 5 })]   // T2 stands between them
    [InlineData(new[] { 1, 3 })]   // different rows
    [InlineData(new[] { 5, 6 })]
    public void TablesThatAreNotNeighboursInARow_CannotBeMerged(int[] clusterIds)
    {
        var floor = Floor(DefaultDinerLayoutSeed.Create());
        var free = floor.Clusters.Select(c => c.ClusterId).ToHashSet();

        TableMergeRules.IsValidMerge(clusterIds, floor.Tables, floor.Clusters, free, MergeGeometry.For(floor, 1)).Should().BeFalse();
    }

    [Fact]
    public void TheDefaultDinerConfig_AllowsTheGapAndSetsTheMoveSpeed()
    {
        var config = DefaultDinerConfigSeed.Create();

        TableMoveTiming.MaxMergeGapCells(config).Should().Be(1);
        TableMoveTiming.SecondsPerCell(config).Should().Be(2f);
    }

    private static FloorState Floor(RestaurantLayout layout) => new()
    {
        Tables = layout.Tables.ToList(),
        Clusters = layout.Clusters.ToList(),
        KitchenStations = layout.KitchenStations.ToList(),
        HeatLamps = layout.HeatLamps.ToList(),
        Freezers = layout.Freezers.ToList(),
        Bathrooms = layout.Bathrooms.ToList()
    };
}
