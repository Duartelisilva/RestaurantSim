using FluentAssertions;
using RestaurantSim.Client.Components;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.Components;

public class FloorLabelsTests
{
    [Fact]
    public void Occupancy_FreeCluster_IsNull()
    {
        var snapshot = BuildSnapshot(occupiedBy: null);

        FloorLabels.Occupancy(snapshot, snapshot.Floor.Tables[0]).Should().BeNull();
    }

    [Fact]
    public void Occupancy_KnownGroup_ShowsIdAndPartySize()
    {
        var snapshot = BuildSnapshot(occupiedBy: 7, active: [new ActiveGroupDto(7, 3, 1, "Seated", "Seated")]);

        FloorLabels.Occupancy(snapshot, snapshot.Floor.Tables[0]).Should().Be("G7 ×3");
    }

    [Fact]
    public void Occupancy_GroupUnknownToClient_ShowsIdOnly()
    {
        var snapshot = BuildSnapshot(occupiedBy: 7);

        FloorLabels.Occupancy(snapshot, snapshot.Floor.Tables[0]).Should().Be("G7");
    }

    [Fact]
    public void Occupancy_MergedCluster_LabelsOnlyTheLowestNumberedTable()
    {
        var snapshot = BuildSnapshot(occupiedBy: 7, active: [new ActiveGroupDto(7, 4, 1, "Seated", "Seated")]);

        FloorLabels.Occupancy(snapshot, snapshot.Floor.Tables[0]).Should().Be("G7 ×4");
        FloorLabels.Occupancy(snapshot, snapshot.Floor.Tables[1]).Should().BeNull();
    }

    [Fact]
    public void Occupancy_TableWithoutCluster_IsNull()
    {
        var snapshot = BuildSnapshot(occupiedBy: 7);
        var loose = new TableDto(99, new GridPointDto(9, 9), new GridSizeDto(1, 1), null, true);

        FloorLabels.Occupancy(snapshot, loose).Should().BeNull();
    }

    // One merged cluster (id 1) of two tables (ids 1 and 2).
    private static RestaurantSnapshotDto BuildSnapshot(int? occupiedBy, IReadOnlyList<ActiveGroupDto>? active = null) => new(
        Guid.NewGuid(), 0, 1, 1, 0, 0m, 0f,
        new FloorDto(
            [
                new TableDto(1, new GridPointDto(0, 0), new GridSizeDto(1, 1), 1, true),
                new TableDto(2, new GridPointDto(1, 0), new GridSizeDto(1, 1), 1, true)
            ],
            [new ClusterDto(1, [1, 2], 4, occupiedBy is null ? "Free" : "Occupied", occupiedBy)],
            [], [], [], []),
        [], [], active ?? []);
}
