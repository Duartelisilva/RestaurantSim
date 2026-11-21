using FluentAssertions;
using NSubstitute;
using RestaurantSim.Client.Engine;
using RestaurantSim.Client.State;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Hubs;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.State;

public class EngineSessionStaffTests
{
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly IEngineClient _engine = Substitute.For<IEngineClient>();
    private readonly RestaurantStore _store = new();
    private readonly EngineSession _session;

    public EngineSessionStaffTests()
    {
        _engine.CreateRestaurantAsync("default_diner", Arg.Any<CancellationToken>())
            .Returns(HubResult<Guid>.Ok(_restaurantId));
        _engine.JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>())
            .Returns(HubResult<RestaurantSnapshotDto>.Ok(BuildSnapshot()));

        _session = new EngineSession(_engine, _store);
    }

    [Fact]
    public async Task StaffStateChanged_UpdatesTheStore_WithoutALogLineAsync()
    {
        await _session.StartAsync("default_diner");
        var logLines = _session.Log.Count;

        _engine.StaffStateChanged += Raise.Event<Action<StaffStateChangedDto>>(
            new StaffStateChangedDto(2, StaffStateNames.Busy, TaskTypeNames.CleanTable, 3, 0.2f, 701));

        _store.Snapshot!.Staff.Single().State.Should().Be(StaffStateNames.Busy);
        _session.Log.Count.Should().Be(logLines);
    }

    private static RestaurantSnapshotDto BuildSnapshot() => new(
        Guid.NewGuid(), 690, 1, 1, 0, 0m, 0f,
        new FloorDto([], [], [], [], [], []),
        [new StaffDto(2, "Busser 1", StaffRoleNames.Busser, StaffStateNames.Idle, new GridPointDto(6, 5), null, null, 0f)],
        [], []);
}
