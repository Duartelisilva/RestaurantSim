using FluentAssertions;
using NSubstitute;
using RestaurantSim.Client.Engine;
using RestaurantSim.Client.State;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Hubs;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.State;

// The fake engine returns already-completed tasks, so the session's
// fire-and-forget resync finishes synchronously inside Raise.Event — no
// delays or polling needed.
public class EngineSessionTests
{
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly IEngineClient _engine = Substitute.For<IEngineClient>();
    private readonly RestaurantStore _store = new();
    private readonly EngineSession _session;

    public EngineSessionTests()
    {
        _engine.CreateRestaurantAsync("default_diner", Arg.Any<CancellationToken>())
            .Returns(HubResult<Guid>.Ok(_restaurantId));
        _engine.JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>())
            .Returns(HubResult<RestaurantSnapshotDto>.Ok(BuildSnapshot(tick: 690)));

        _session = new EngineSession(_engine, _store);
    }

    [Fact]
    public async Task StartAsync_ConnectsCreatesJoinsAndLoadsTheStore()
    {
        await _session.StartAsync("default_diner");

        await _engine.Received(1).ConnectAsync(Arg.Any<CancellationToken>());
        await _engine.Received(1).CreateRestaurantAsync("default_diner", Arg.Any<CancellationToken>());
        await _engine.Received(1).JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>());
        _store.Snapshot.Should().NotBeNull();
        _session.Clock.Should().NotBeNull();
        _session.Status.Should().Be("Joined");
        _session.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenCreateFails_ReportsErrorAndStaysStartable()
    {
        _engine.CreateRestaurantAsync("default_diner", Arg.Any<CancellationToken>())
            .Returns(HubResult<Guid>.Fail("unknown config"));

        await _session.StartAsync("default_diner");

        _session.Status.Should().Contain("unknown config");
        _store.Snapshot.Should().BeNull();
        _session.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_OnlyRunsOnce()
    {
        await _session.StartAsync("default_diner");
        await _session.StartAsync("default_diner");

        await _engine.Received(1).CreateRestaurantAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushedEvents_AreRoutedIntoTheStoreAndLogged()
    {
        await _session.StartAsync("default_diner");

        _engine.GroupArrived += Raise.Event<Action<GroupArrivedDto>>(new GroupArrivedDto(7, 3, 700));

        _store.Snapshot!.Queue.Should().ContainSingle().Which.GroupId.Should().Be(7);
        _session.Log.Should().ContainSingle().Which.Should().Contain("group 7 arrived");
    }

    [Fact]
    public async Task BillPaid_UpdatesCashInTheStoreAndLogsIt()
    {
        await _session.StartAsync("default_diner");

        _engine.BillPaid += Raise.Event<Action<BillPaidDto>>(new BillPaidDto(7, 12.50m, 3.10m, 5012.50m, 700));

        _store.Snapshot!.Cash.Should().Be(5012.50m);
        _session.Log.Should().ContainSingle().Which.Should().Contain("group 7 paid");
    }

    [Fact]
    public async Task GroupTurnedAway_UpdatesReputationInTheStore()
    {
        await _session.StartAsync("default_diner");

        _engine.GroupTurnedAway += Raise.Event<Action<GroupTurnedAwayDto>>(new GroupTurnedAwayDto(19.8f, 700));

        _store.Snapshot!.Reputation.Should().Be(19.8f);
    }

    [Fact]
    public async Task Reconnect_RejoinsAndReloadsSnapshotAndClock()
    {
        await _session.StartAsync("default_diner");
        var firstClock = _session.Clock;
        _engine.JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>())
            .Returns(HubResult<RestaurantSnapshotDto>.Ok(BuildSnapshot(tick: 800)));

        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Reconnecting);
        _session.Status.Should().Contain("reconnecting");

        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Connected);

        await _engine.Received(2).JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>());
        _store.Snapshot!.SimulationTick.Should().Be(800);
        _session.Clock.Should().NotBeSameAs(firstClock);
        _session.Status.Should().Be("Joined");
        _session.Log.Should().Contain(l => l.Contains("resynced"));
    }

    [Fact]
    public async Task Connected_WithoutAPriorDrop_DoesNotRejoin()
    {
        await _session.StartAsync("default_diner");

        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Connected);

        await _engine.Received(1).JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drop_BeforeAnyRestaurantIsJoined_NeedsNoResync()
    {
        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Reconnecting);
        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Connected);

        _engine.DidNotReceive().JoinRestaurantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedResync_IsRetriedOnTheNextConnected()
    {
        await _session.StartAsync("default_diner");
        _engine.JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>())
            .Returns(
                HubResult<RestaurantSnapshotDto>.Fail("restaurant gone"),
                HubResult<RestaurantSnapshotDto>.Ok(BuildSnapshot(tick: 900)));

        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Reconnecting);
        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Connected);
        _session.Status.Should().Contain("restaurant gone");

        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Connected);

        _store.Snapshot!.SimulationTick.Should().Be(900);
        _session.Status.Should().Be("Joined");
    }

    [Fact]
    public async Task CanReconnect_OnlyWhenDisconnectedWithAJoinedRestaurant()
    {
        _session.CanReconnect.Should().BeFalse();

        await _session.StartAsync("default_diner");
        _session.CanReconnect.Should().BeFalse();

        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Reconnecting);
        _session.CanReconnect.Should().BeFalse();

        _engine.ConnectionStateChanged += Raise.Event<Action<ConnectionState>>(ConnectionState.Disconnected);
        _session.CanReconnect.Should().BeTrue();
    }

    private static RestaurantSnapshotDto BuildSnapshot(long tick) => new(
        Guid.NewGuid(), tick, 1, 1, 0, 0m, 0f,
        new FloorDto([], [], [], [], [], []),
        [], [], []);
}
