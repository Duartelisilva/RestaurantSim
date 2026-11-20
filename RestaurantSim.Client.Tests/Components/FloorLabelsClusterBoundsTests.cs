using FluentAssertions;
using RestaurantSim.Client.Components;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.Components;

public class FloorLabelsClusterBoundsTests
{
    [Fact]
    public void ClusterBounds_WrapsAllTablesOfTheCluster()
    {
        var snapshot = BuildSnapshot(
            new TableDto(3, new GridPointDto(0, 2), new GridSizeDto(1, 1), 3, true, false),
            new TableDto(4, new GridPointDto(1, 2), new GridSizeDto(1, 1), 3, true, false),
            new TableDto(9, new GridPointDto(8, 8), new GridSizeDto(1, 1), 9, true, false));

        var bounds = FloorLabels.ClusterBounds(snapshot, new ClusterDto(3, [3, 4], 4, "Free", null));

        bounds.Should().Be((0, 2, 2, 1));
    }

    [Fact]
    public void ClusterBounds_ForABiggerHitbox_UsesTheFarEdge()
    {
        var snapshot = BuildSnapshot(new TableDto(1, new GridPointDto(2, 3), new GridSizeDto(2, 2), 1, true, false));

        FloorLabels.ClusterBounds(snapshot, new ClusterDto(1, [1], 4, "Free", null)).Should().Be((2, 3, 2, 2));
    }

    [Fact]
    public void ClusterBounds_WhenTheClientKnowsNoneOfTheTables_IsNull()
    {
        var snapshot = BuildSnapshot();

        FloorLabels.ClusterBounds(snapshot, new ClusterDto(7, [70, 71], 4, "Free", null)).Should().BeNull();
    }

    private static RestaurantSnapshotDto BuildSnapshot(params TableDto[] tables) => new(
        Guid.NewGuid(), 10, 1, 1, 0, 0m, 0f,
        new FloorDto(tables, [], [], [], [], []),
        [], [], []);
}
