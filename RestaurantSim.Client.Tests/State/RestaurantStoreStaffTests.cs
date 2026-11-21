using FluentAssertions;
using RestaurantSim.Client.State;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.State;

public class RestaurantStoreStaffTests
{
    private readonly RestaurantStore _store = new();

    [Fact]
    public void StaffStateChanged_SetsStateTaskAndFatigue_AndNothingElse()
    {
        _store.Load(BuildSnapshot());

        var applied = _store.Apply(new StaffStateChangedDto(2, StaffStateNames.Busy, TaskTypeNames.CleanTable, 3, 0.3f, 12));

        applied.Should().BeTrue();
        var busser = _store.Snapshot!.Staff.Single(s => s.StaffId == 2);
        busser.State.Should().Be(StaffStateNames.Busy);
        busser.TaskType.Should().Be(TaskTypeNames.CleanTable);
        busser.TaskTargetId.Should().Be(3);
        busser.Fatigue.Should().Be(0.3f);
        busser.Position.Should().Be(new GridPointDto(6, 5));
        _store.Snapshot.Staff.Single(s => s.StaffId == 1).State.Should().Be(StaffStateNames.Idle);
    }

    [Fact]
    public void FinishingATask_ClearsTheTask()
    {
        _store.Load(BuildSnapshot());
        _store.Apply(new StaffStateChangedDto(2, StaffStateNames.Busy, TaskTypeNames.CleanTable, 3, 0.3f, 12));

        _store.Apply(new StaffStateChangedDto(2, StaffStateNames.Idle, null, null, 0.31f, 15));

        var busser = _store.Snapshot!.Staff.Single(s => s.StaffId == 2);
        busser.State.Should().Be(StaffStateNames.Idle);
        busser.TaskType.Should().BeNull();
        busser.TaskTargetId.Should().BeNull();
    }

    [Fact]
    public void SameEventTwice_IsIdempotent()
    {
        _store.Load(BuildSnapshot());
        var changed = new StaffStateChangedDto(2, StaffStateNames.Busy, TaskTypeNames.CleanTable, 3, 0.3f, 12);

        _store.Apply(changed);
        var afterFirst = _store.Snapshot;
        _store.Apply(changed);

        _store.Snapshot.Should().BeEquivalentTo(afterFirst);
    }

    [Fact]
    public void AStaffMemberTheClientDoesNotKnow_ChangesNothing()
    {
        _store.Load(BuildSnapshot());
        var before = _store.Snapshot;

        var applied = _store.Apply(new StaffStateChangedDto(99, StaffStateNames.Busy, null, null, 0f, 12));

        applied.Should().BeTrue();
        _store.Snapshot.Should().BeSameAs(before);
    }

    [Fact]
    public void OlderThanTheSnapshot_IsIgnored()
    {
        _store.Load(BuildSnapshot(tick: 10));

        var applied = _store.Apply(new StaffStateChangedDto(2, StaffStateNames.Busy, null, null, 0f, 9));

        applied.Should().BeFalse();
        _store.Snapshot!.Staff.Single(s => s.StaffId == 2).State.Should().Be(StaffStateNames.Idle);
    }

    private static RestaurantSnapshotDto BuildSnapshot(long tick = 10) => new(
        Guid.NewGuid(), tick, 1, 1, 0, 0m, 0f,
        new FloorDto([], [], [], [], [], []),
        [
            new StaffDto(1, "Host 1", StaffRoleNames.Host, StaffStateNames.Idle, new GridPointDto(8, 3), null, null, 0f),
            new StaffDto(2, "Busser 1", StaffRoleNames.Busser, StaffStateNames.Idle, new GridPointDto(6, 5), null, null, 0f)
        ],
        [], []);
}
