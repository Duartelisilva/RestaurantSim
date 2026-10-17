using FluentAssertions;
using RestaurantSim.Client.State;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.State;

public class RestaurantStoreTests
{
    private readonly RestaurantStore _store = new();

    [Fact]
    public void Load_SetsSnapshotAndRaisesChanged()
    {
        var raised = 0;
        _store.Changed += () => raised++;
        var snapshot = BuildSnapshot();

        _store.Load(snapshot);

        _store.Snapshot.Should().BeSameAs(snapshot);
        raised.Should().Be(1);
    }

    [Fact]
    public void Apply_BeforeLoad_IsIgnored()
    {
        _store.Apply(new GroupSeatedDto(7, 1, 10)).Should().BeFalse();

        _store.Snapshot.Should().BeNull();
    }

    // --- GroupArrived / GroupAbandonedQueue ---

    [Fact]
    public void Apply_GroupArrived_AddsToQueue()
    {
        _store.Load(BuildSnapshot());

        _store.Apply(new GroupArrivedDto(7, 3, 11)).Should().BeTrue();

        var queued = _store.Snapshot!.Queue.Should().ContainSingle().Subject;
        queued.GroupId.Should().Be(7);
        queued.PartySize.Should().Be(3);
    }

    [Fact]
    public void Apply_GroupArrived_IsIdempotent_AndIgnoresGroupsAlreadyKnown()
    {
        _store.Load(BuildSnapshot(queue: [new QueuedGroupDto(7, 3)]));

        _store.Apply(new GroupArrivedDto(7, 3, 11));
        _store.Apply(new GroupArrivedDto(7, 3, 11));

        _store.Snapshot!.Queue.Should().ContainSingle();
    }

    [Fact]
    public void Apply_GroupAbandonedQueue_RemovesFromQueue()
    {
        _store.Load(BuildSnapshot(queue: [new QueuedGroupDto(7, 3), new QueuedGroupDto(8, 2)]));

        _store.Apply(new GroupAbandonedQueueDto(7, 19.5f, 11));

        _store.Snapshot!.Queue.Should().ContainSingle().Which.GroupId.Should().Be(8);
        _store.Snapshot.Reputation.Should().Be(19.5f);
    }

    // --- Cash / Reputation (absolute values, idempotent) ---

    [Fact]
    public void Apply_BillPaid_SetsCashToTheAbsoluteValue()
    {
        _store.Load(BuildSnapshot());

        _store.Apply(new BillPaidDto(7, 12.50m, 3.10m, 5012.50m, 20)).Should().BeTrue();

        _store.Snapshot!.Cash.Should().Be(5012.50m);
    }

    [Fact]
    public void Apply_BillPaid_SameEventTwice_DoesNotDoubleCount()
    {
        _store.Load(BuildSnapshot());
        var paid = new BillPaidDto(7, 12.50m, 3.10m, 5012.50m, 20);

        _store.Apply(paid);
        _store.Apply(paid);

        _store.Snapshot!.Cash.Should().Be(5012.50m);
    }

    [Fact]
    public void Apply_BillPaid_OlderThanSnapshot_IsIgnored()
    {
        _store.Load(BuildSnapshot(tick: 10));

        _store.Apply(new BillPaidDto(7, 12.50m, 3.10m, 5012.50m, 9)).Should().BeFalse();

        _store.Snapshot!.Cash.Should().Be(0m);
    }

    [Fact]
    public void Apply_GroupTurnedAway_SetsReputation()
    {
        _store.Load(BuildSnapshot());

        _store.Apply(new GroupTurnedAwayDto(19.8f, 11)).Should().BeTrue();

        _store.Snapshot!.Reputation.Should().Be(19.8f);
    }

    // --- GroupSeated ---

    [Fact]
    public void Apply_GroupSeated_OccupiesClusterAndMovesGroupFromQueueToActive()
    {
        _store.Load(BuildSnapshot(queue: [new QueuedGroupDto(7, 3)]));

        var applied = _store.Apply(new GroupSeatedDto(7, 1, 10));

        applied.Should().BeTrue();
        var snapshot = _store.Snapshot!;
        snapshot.Floor.Clusters[0].State.Should().Be("Occupied");
        snapshot.Floor.Clusters[0].OccupiedByGroupId.Should().Be(7);
        snapshot.Floor.Clusters[1].State.Should().Be("Free");
        snapshot.Queue.Should().BeEmpty();
        var active = snapshot.ActiveGroups.Should().ContainSingle().Subject;
        active.GroupId.Should().Be(7);
        active.PartySize.Should().Be(3);
        active.ClusterId.Should().Be(1);
    }

    [Fact]
    public void Apply_EventOlderThanSnapshot_IsIgnored()
    {
        _store.Load(BuildSnapshot(tick: 10, queue: [new QueuedGroupDto(7, 3)]));

        var applied = _store.Apply(new GroupSeatedDto(7, 1, 9));

        applied.Should().BeFalse();
        _store.Snapshot!.Floor.Clusters[0].State.Should().Be("Free");
        _store.Snapshot.Queue.Should().ContainSingle();
    }

    [Fact]
    public void Apply_GroupSeated_SameEventTwice_IsIdempotent()
    {
        _store.Load(BuildSnapshot(queue: [new QueuedGroupDto(7, 3)]));
        var seated = new GroupSeatedDto(7, 1, 10);

        _store.Apply(seated);
        _store.Apply(seated);

        _store.Snapshot!.ActiveGroups.Should().ContainSingle();
        _store.Snapshot.Floor.Clusters[0].OccupiedByGroupId.Should().Be(7);
    }

    [Fact]
    public void Apply_GroupSeated_GroupUnknownToClient_OccupiesClusterWithoutInventingAnActiveGroup()
    {
        _store.Load(BuildSnapshot());

        _store.Apply(new GroupSeatedDto(99, 2, 11));

        _store.Snapshot!.Floor.Clusters[1].State.Should().Be("Occupied");
        _store.Snapshot.ActiveGroups.Should().BeEmpty();
    }

    // --- GroupLeft / TableCleaned ---

    [Fact]
    public void Apply_GroupLeft_FreesGroupMarksClusterTablesDirty()
    {
        _store.Load(BuildSnapshot(queue: [new QueuedGroupDto(7, 3)]));
        _store.Apply(new GroupSeatedDto(7, 2, 10));

        _store.Apply(new GroupLeftDto(7, 2, 20));

        var snapshot = _store.Snapshot!;
        snapshot.ActiveGroups.Should().BeEmpty();
        var cluster = snapshot.Floor.Clusters[1];
        cluster.OccupiedByGroupId.Should().BeNull();
        cluster.State.Should().Be("NeedsCleaning");
        snapshot.Floor.Tables.Where(t => t.ClusterId == 2).Should().OnlyContain(t => !t.IsClean);
        snapshot.Floor.Tables.Single(t => t.ClusterId == 1).IsClean.Should().BeTrue();
    }

    [Fact]
    public void Apply_TableCleaned_ClusterStaysDirtyUntilEveryTableIsClean()
    {
        _store.Load(BuildSnapshot());
        _store.Apply(new GroupSeatedDto(7, 2, 10));
        _store.Apply(new GroupLeftDto(7, 2, 20));

        _store.Apply(new TableCleanedDto(2, 30));
        _store.Snapshot!.Floor.Clusters[1].State.Should().Be("NeedsCleaning");

        _store.Apply(new TableCleanedDto(3, 31));
        _store.Snapshot.Floor.Clusters[1].State.Should().Be("Free");
    }

    // Cluster 1: one table (id 1). Cluster 2: a merged pair of tables (ids 2 and 3).
    private static RestaurantSnapshotDto BuildSnapshot(long tick = 10, IReadOnlyList<QueuedGroupDto>? queue = null) => new(
        Guid.NewGuid(), tick, 1, 1, 0, 0m, 0f,
        new FloorDto(
            [
                new TableDto(1, new GridPointDto(0, 0), new GridSizeDto(1, 1), 1, true),
                new TableDto(2, new GridPointDto(2, 0), new GridSizeDto(1, 1), 2, true),
                new TableDto(3, new GridPointDto(3, 0), new GridSizeDto(1, 1), 2, true)
            ],
            [new ClusterDto(1, [1], 2, "Free", null), new ClusterDto(2, [2, 3], 4, "Free", null)],
            [], [], [], []),
        [],
        queue ?? [],
        []);
}
