using System.Numerics;
using FluentAssertions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Domain.Tests.Floor;

public class TableMovementTests
{
    // ---------- Table position / home ----------

    [Fact]
    public void Table_FirstPosition_IsAlsoTheHomePosition()
    {
        var table = Tbl(1, 4, 2);

        table.HomePosition.Should().Be(new GridPosition(4, 2));
    }

    [Fact]
    public void Table_MovingChangesPosition_NotHomePosition()
    {
        var table = Tbl(1, 4, 2);

        table.Position = new GridPosition(3, 2);

        table.Position.Should().Be(new GridPosition(3, 2));
        table.HomePosition.Should().Be(new GridPosition(4, 2));
    }

    [Fact]
    public void Table_ExplicitHomePosition_Wins()
    {
        var table = new Table
        {
            TableId = 1, Position = new GridPosition(3, 2), HomePosition = new GridPosition(4, 2),
            Hitbox = new GridBounds(1, 1), Shape = Shape(), Seats = Array.Empty<Seat>()
        };

        table.HomePosition.Should().Be(new GridPosition(4, 2));
        table.Position.Should().Be(new GridPosition(3, 2));
    }

    [Fact]
    public void Table_IsMoving_OnlyWhileAnEndTickIsSet()
    {
        var table = Tbl(1, 0, 0);
        table.IsMoving.Should().BeFalse();

        table.MoveEndTick = 50;

        table.IsMoving.Should().BeTrue();
    }

    // ---------- cluster state ----------

    [Fact]
    public void ClusterState_AMemberTableMoving_IsMoving_EvenWhenDirtyAndOccupied()
    {
        var tables = new List<Table> { Tbl(1, 0, 0), Tbl(2, 1, 0) };
        tables[1].MoveEndTick = 50;
        tables[0].IsClean = false;
        var cluster = new TableCluster { ClusterId = 1, TableIds = [1, 2], Capacity = 4, OccupiedBy = 7 };

        cluster.DeriveState(tables, Array.Empty<Reservation>(), 10).Should().Be(TableClusterState.Moving);
    }

    [Fact]
    public void ClusterState_NoTableMoving_FallsBackToTheUsualOrder()
    {
        var tables = new List<Table> { Tbl(1, 0, 0) };
        var cluster = new TableCluster { ClusterId = 1, TableIds = [1], Capacity = 2 };

        cluster.DeriveState(tables, Array.Empty<Reservation>(), 10).Should().Be(TableClusterState.Free);
    }

    [Fact]
    public void CanSeat_MovingCluster_IsNeverSeatable()
    {
        var tables = new List<Table> { Tbl(1, 0, 0) };
        tables[0].MoveEndTick = 50;
        var cluster = new TableCluster { ClusterId = 1, TableIds = [1], Capacity = 2 };

        cluster.CanSeat(2, tables, Array.Empty<Reservation>(), 10).Should().BeFalse();
    }

    // ---------- footprint ----------

    [Fact]
    public void Footprint_PlainHitbox_CoversEveryCellInsideIt()
    {
        Footprint.Cells(new GridPosition(2, 3), new GridBounds(2, 1), null)
            .Should().BeEquivalentTo(new[] { new GridPosition(2, 3), new GridPosition(3, 3) });
    }

    [Fact]
    public void Footprint_OccupiedCells_AreRelativeToThePosition()
    {
        Footprint.Cells(new GridPosition(2, 3), new GridBounds(2, 2), new[] { new GridPosition(0, 0), new GridPosition(1, 1) })
            .Should().BeEquivalentTo(new[] { new GridPosition(2, 3), new GridPosition(3, 4) });
    }

    // ---------- timing ----------

    [Fact]
    public void DurationTicks_NoMoves_IsZero()
    {
        TableMoveTiming.DurationTicks(Array.Empty<TableMove>(), Config()).Should().Be(0);
    }

    [Fact]
    public void DurationTicks_UsesTheLongestSlide_AtTheConfiguredSecondsPerCell()
    {
        var moves = new[] { Move(1, 0, 1), Move(2, 0, 3) };

        TableMoveTiming.DurationTicks(moves, Config(secondsPerCell: 1.5f)).Should().Be(5); // 3 cells x 1.5 s = 4.5 -> 5
    }

    [Fact]
    public void DurationTicks_FallsBackToTheDefaultSecondsPerCell()
    {
        TableMoveTiming.DurationTicks(new[] { Move(1, 0, 1) }, Config())
            .Should().Be((long)TableMoveTiming.DefaultSecondsPerCell);
    }

    [Fact]
    public void DurationTicks_ScalesWithTicksPerRealSecond_SoRealTimeIsUnchanged()
    {
        TableMoveTiming.DurationTicks(new[] { Move(1, 0, 1) }, Config(secondsPerCell: 2f, ticksPerSecond: 4))
            .Should().Be(8);
    }

    [Fact]
    public void DurationTicks_NeverResolvesToZeroTicks()
    {
        TableMoveTiming.DurationTicks(new[] { Move(1, 0, 1) }, Config(secondsPerCell: 0.01f)).Should().Be(1);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, 1)]
    [InlineData(-3, 0)]
    public void MaxMergeGapCells_IsNeverNegative_AndDefaultsToTouchingOnly(int? configured, int expected)
    {
        TableMoveTiming.MaxMergeGapCells(Config(maxGap: configured)).Should().Be(expected);
    }

    // ---------- helpers ----------

    private static TableMove Move(int id, int fromX, int toX) =>
        new(id, new GridPosition(fromX, 0), new GridPosition(toX, 0));

    private static Table Tbl(int id, int x, int y) => new()
    {
        TableId = id, Position = new GridPosition(x, y), Hitbox = new GridBounds(1, 1),
        Shape = Shape(), Seats = Array.Empty<Seat>(), ClusterId = id
    };

    private static TableShape Shape() => new()
    {
        ShapeId = "test_2top", CanMerge = true,
        SeatDefinitions = new List<SeatDefinition>
        {
            new(0, new Vector2(0, -0.5f), Orientation.South),
            new(1, new Vector2(0, 0.5f), Orientation.North)
        }
    };

    private static RestaurantConfig Config(float? secondsPerCell = null, int ticksPerSecond = 1, int? maxGap = null) => new()
    {
        ConfigId = "t", StartingCash = 0m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = ticksPerSecond,
        TableLayoutId = "l", StaffRosterSeed = Array.Empty<(StaffRole, int)>(), MenuItemIds = new[] { 1 },
        ReputationStart = 20f, QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f,
        NaturalMoodRecoveryPerSecond = 0.5f, ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f,
        MaxCallAttempts = 3, ActiveMealPeriods = new List<MealPeriodDefinition>(), EventModuleIds = Array.Empty<string>(),
        TipBaseRate = 50f, TipMoodExponent = 0.75f, MaxQueueLength = 12, BaseArrivalIntervalSeconds = 15f,
        TableMoveSecondsPerCell = secondsPerCell, MaxMergeGapCells = maxGap
    };
}
