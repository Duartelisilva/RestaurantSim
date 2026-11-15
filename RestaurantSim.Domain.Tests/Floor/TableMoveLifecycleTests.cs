using System.Numerics;
using FluentAssertions;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Domain.Tests.Floor;

public class TableMoveLifecycleTests
{
    [Fact]
    public void StartMove_FlipsThePositionNow_AndMarksTheSlide()
    {
        var table = Tbl(1, 2, 0);

        table.StartMove(new GridPosition(1, 0), endTick: 50);

        table.Position.Should().Be(new GridPosition(1, 0));
        table.MoveFrom.Should().Be(new GridPosition(2, 0));
        table.MoveEndTick.Should().Be(50);
        table.IsMoving.Should().BeTrue();
        table.HomePosition.Should().Be(new GridPosition(2, 0));
    }

    [Fact]
    public void CompleteMove_ClearsTheMarkers_AndKeepsThePosition()
    {
        var table = Tbl(1, 2, 0);
        table.StartMove(new GridPosition(1, 0), endTick: 50);

        table.CompleteMove();

        table.IsMoving.Should().BeFalse();
        table.MoveFrom.Should().BeNull();
        table.MoveEndTick.Should().BeNull();
        table.Position.Should().Be(new GridPosition(1, 0));
    }

    [Fact]
    public void MergeGeometry_ForFloor_UsesTheFloorsOwnGapLimit()
    {
        var floor = new FloorState { MaxMergeGapCells = 2 };
        floor.Tables.Add(Tbl(1, 0, 0));

        MergeGeometry.For(floor).MaxGapCells.Should().Be(2);
        new FloorState().MaxMergeGapCells.Should().Be(0);
    }

    [Fact]
    public void SeatedGroupsByWaiter_CountsOccupiedClustersPerAssignedWaiter()
    {
        var restaurant = Restaurant();
        restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = 1, TableIds = [1], Capacity = 2, OccupiedBy = 5, AssignedWaiterId = 10 });
        restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = 2, TableIds = [2], Capacity = 2, OccupiedBy = 6, AssignedWaiterId = 10 });
        restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = 3, TableIds = [3], Capacity = 2, OccupiedBy = 7, AssignedWaiterId = 20 });
        restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = 4, TableIds = [4], Capacity = 2, AssignedWaiterId = 20 });   // empty
        restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = 5, TableIds = [5], Capacity = 2, OccupiedBy = 8 });          // no waiter

        var load = WaiterWorkload.SeatedGroupsByWaiter(restaurant);

        load.Should().BeEquivalentTo(new Dictionary<int, int> { [10] = 2, [20] = 1 });
    }

    private static Restaurant Restaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "t", SimulationTick = 1,
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };

    private static Table Tbl(int id, int x, int y) => new()
    {
        TableId = id, Position = new GridPosition(x, y), Hitbox = new GridBounds(1, 1),
        Shape = new TableShape
        {
            ShapeId = "t", CanMerge = true,
            SeatDefinitions = new List<SeatDefinition> { new(0, new Vector2(0, -0.5f), Orientation.South) }
        },
        Seats = Array.Empty<Seat>(), ClusterId = id
    };
}
