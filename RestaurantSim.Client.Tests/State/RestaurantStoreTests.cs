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
    public void Apply_SameEventTwice_IsIdempotent()
    {
        _store.Load(BuildSnapshot(queue: [new QueuedGroupDto(7, 3)]));
        var seated = new GroupSeatedDto(7, 1, 10);

        _store.Apply(seated);
        _store.Apply(seated);

        _store.Snapshot!.ActiveGroups.Should().ContainSingle();
        _store.Snapshot.Floor.Clusters[0].OccupiedByGroupId.Should().Be(7);
    }

    [Fact]
    public void Apply_GroupUnknownToClient_StillOccupiesClusterWithoutInventingAnActiveGroup()
    {
        _store.Load(BuildSnapshot());

        _store.Apply(new GroupSeatedDto(99, 2, 11));

        _store.Snapshot!.Floor.Clusters[1].State.Should().Be("Occupied");
        _store.Snapshot.ActiveGroups.Should().BeEmpty();
    }

    private static RestaurantSnapshotDto BuildSnapshot(long tick = 10, IReadOnlyList<QueuedGroupDto>? queue = null) => new(
        Guid.NewGuid(), tick, 1, 1, 0, 0m, 0f,
        new FloorDto(
            [],
            [new ClusterDto(1, [1], 2, "Free", null), new ClusterDto(2, [2], 2, "Free", null)],
            [], [], [], []),
        [],
        queue ?? [],
        []);
}
