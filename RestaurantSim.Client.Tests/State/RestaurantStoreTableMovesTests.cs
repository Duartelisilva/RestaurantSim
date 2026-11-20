using FluentAssertions;
using RestaurantSim.Client.State;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.State;

// Merge / split / move-completed: the client mirrors the server's resulting topology, marks
// the tables that slide, and remembers where they came from so the floor view can animate.
public class RestaurantStoreTableMovesTests
{
    private readonly RestaurantStore _store = new();

    // ---------- merge ----------

    [Fact]
    public void Merge_ReplacesTheMergedClusters_AndTheSlidingTableMovesAndIsMarked()
    {
        _store.Load(BuildRow(ticksPerSecond: 2));

        var applied = _store.Apply(Merged(clusterId: 1, ids: [1, 2], tables: [1, 2], capacity: 4, duration: 6, moves: [Move(2, 2, 1)]));

        applied.Should().BeTrue();
        var floor = _store.Snapshot!.Floor;
        floor.Clusters.Select(c => c.ClusterId).Should().Equal(1, 3);
        var merged = floor.Clusters.Single(c => c.ClusterId == 1);
        merged.TableIds.Should().Equal(1, 2);
        merged.Capacity.Should().Be(4);
        merged.State.Should().Be("Moving");

        var slider = floor.Tables.Single(t => t.TableId == 2);
        slider.Position.Should().Be(new GridPointDto(1, 0));
        slider.ClusterId.Should().Be(1);
        slider.IsMoving.Should().BeTrue();
        floor.Tables.Single(t => t.TableId == 1).IsMoving.Should().BeFalse();

        _store.Motions.Should().ContainKey(2);
        _store.Motions[2].From.Should().Be(new GridPointDto(2, 0));
        _store.Motions[2].To.Should().Be(new GridPointDto(1, 0));
        _store.Motions[2].DurationSeconds.Should().Be(3); // 6 ticks at 2 ticks per real second
    }

    [Fact]
    public void Merge_ThatNeededNoMove_IsFreeAtOnce_WithNoAnimation()
    {
        _store.Load(BuildRow());

        _store.Apply(Merged(clusterId: 1, ids: [1, 2], tables: [1, 2], capacity: 4, duration: 0, moves: []));

        _store.Snapshot!.Floor.Clusters.Single(c => c.ClusterId == 1).State.Should().Be("Free");
        _store.Motions.Should().BeEmpty();
    }

    [Fact]
    public void Merge_AppliedTwice_IsIdempotent()
    {
        _store.Load(BuildRow());
        var merged = Merged(clusterId: 1, ids: [1, 2], tables: [1, 2], capacity: 4, duration: 2, moves: [Move(2, 2, 1)]);

        _store.Apply(merged);
        var afterFirst = _store.Snapshot;
        var motionsAfterFirst = _store.Motions;
        _store.Apply(merged);

        _store.Snapshot.Should().BeEquivalentTo(afterFirst);
        _store.Motions.Should().BeEquivalentTo(motionsAfterFirst);
    }

    [Fact]
    public void Merge_OlderThanTheSnapshot_IsIgnored()
    {
        _store.Load(BuildRow(tick: 10));

        var applied = _store.Apply(Merged(1, [1, 2], [1, 2], 4, 2, [Move(2, 2, 1)], tick: 9));

        applied.Should().BeFalse();
        _store.Snapshot!.Floor.Clusters.Should().HaveCount(3);
    }

    // ---------- move completed ----------

    [Fact]
    public void MoveCompleted_MakesTheClusterUsable_AndEndsTheAnimation()
    {
        _store.Load(BuildRow());
        _store.Apply(Merged(1, [1, 2], [1, 2], 4, 2, [Move(2, 2, 1)]));

        var applied = _store.Apply(new TableMoveCompletedDto(1, 12));

        applied.Should().BeTrue();
        var floor = _store.Snapshot!.Floor;
        floor.Clusters.Single(c => c.ClusterId == 1).State.Should().Be("Free");
        floor.Tables.Should().OnlyContain(t => !t.IsMoving);
        _store.Motions.Should().BeEmpty();
    }

    [Fact]
    public void MoveCompleted_ForAClusterTheClientDoesNotKnow_ChangesNothing()
    {
        _store.Load(BuildRow());
        var before = _store.Snapshot;

        var applied = _store.Apply(new TableMoveCompletedDto(99, 12));

        applied.Should().BeTrue();
        _store.Snapshot.Should().BeSameAs(before);
    }

    [Fact]
    public void AMovingClusterStaysMoving_WhenSomethingElseTriggersARecompute()
    {
        // TableCleaned recomputes every cluster's state; a sliding cluster must not become Free.
        _store.Load(BuildRow());
        _store.Apply(Merged(1, [1, 2], [1, 2], 4, 2, [Move(2, 2, 1)]));

        _store.Apply(new TableCleanedDto(3, 11));

        _store.Snapshot!.Floor.Clusters.Single(c => c.ClusterId == 1).State.Should().Be("Moving");
    }

    // ---------- split ----------

    [Fact]
    public void Split_RestoresTheClusters_AndTheTablesSlideBackHome()
    {
        _store.Load(BuildRow());
        _store.Apply(Merged(1, [1, 2], [1, 2], 4, 2, [Move(2, 2, 1)]));
        _store.Apply(new TableMoveCompletedDto(1, 12));

        var applied = _store.Apply(new ClusterSplitDto(
            1,
            [new ClusterRestoredDto(1, [1], 2), new ClusterRestoredDto(2, [2], 2)],
            [Move(2, 1, 2)],
            2,
            20));

        applied.Should().BeTrue();
        var floor = _store.Snapshot!.Floor;
        floor.Clusters.Select(c => c.ClusterId).Should().Equal(1, 2, 3);
        floor.Clusters.Single(c => c.ClusterId == 1).State.Should().Be("Free");
        floor.Clusters.Single(c => c.ClusterId == 2).State.Should().Be("Moving");
        floor.Clusters.Single(c => c.ClusterId == 2).TableIds.Should().Equal(2);

        var slider = floor.Tables.Single(t => t.TableId == 2);
        slider.Position.Should().Be(new GridPointDto(2, 0));
        slider.ClusterId.Should().Be(2);
        _store.Motions[2].From.Should().Be(new GridPointDto(1, 0));
    }

    [Fact]
    public void Split_AppliedTwice_IsIdempotent()
    {
        _store.Load(BuildRow());
        _store.Apply(Merged(1, [1, 2], [1, 2], 4, 0, []));
        var split = new ClusterSplitDto(1, [new ClusterRestoredDto(1, [1], 2), new ClusterRestoredDto(2, [2], 2)], [], 0, 20);

        _store.Apply(split);
        var afterFirst = _store.Snapshot;
        _store.Apply(split);

        _store.Snapshot.Should().BeEquivalentTo(afterFirst);
    }

    // ---------- snapshots ----------

    [Fact]
    public void LoadingASnapshot_ClearsAnyAnimation()
    {
        _store.Load(BuildRow());
        _store.Apply(Merged(1, [1, 2], [1, 2], 4, 2, [Move(2, 2, 1)]));

        _store.Load(BuildRow(tick: 50));

        _store.Motions.Should().BeEmpty();
    }

    // ---------- helpers ----------

    private static TableMoveDto Move(int tableId, int fromX, int toX) =>
        new(tableId, new GridPointDto(fromX, 0), new GridPointDto(toX, 0));

    private static ClustersMergedDto Merged(
        int clusterId, int[] ids, int[] tables, int capacity, long duration, TableMoveDto[] moves, long tick = 11) =>
        new(clusterId, ids, tables, capacity, moves, duration, tick);

    // Three standalone 2-tops in a row (x = 0, 2, 4), clusters 1..3, all free and clean.
    private static RestaurantSnapshotDto BuildRow(long tick = 10, int ticksPerSecond = 1) => new(
        Guid.NewGuid(), tick, 1, ticksPerSecond, 0, 0m, 0f,
        new FloorDto(
            [
                new TableDto(1, new GridPointDto(0, 0), new GridSizeDto(1, 1), 1, true, false),
                new TableDto(2, new GridPointDto(2, 0), new GridSizeDto(1, 1), 2, true, false),
                new TableDto(3, new GridPointDto(4, 0), new GridSizeDto(1, 1), 3, true, false)
            ],
            [
                new ClusterDto(1, [1], 2, "Free", null),
                new ClusterDto(2, [2], 2, "Free", null),
                new ClusterDto(3, [3], 2, "Free", null)
            ],
            [], [], [], []),
        [], [], []);
}
