using FluentAssertions;
using NSubstitute;
using RestaurantSim.Client.Engine;
using RestaurantSim.Client.State;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Hubs;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.State;

public class EngineSessionTableMovesTests
{
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly IEngineClient _engine = Substitute.For<IEngineClient>();
    private readonly RestaurantStore _store = new();
    private readonly EngineSession _session;

    public EngineSessionTableMovesTests()
    {
        _engine.CreateRestaurantAsync("default_diner", Arg.Any<CancellationToken>())
            .Returns(HubResult<Guid>.Ok(_restaurantId));
        _engine.JoinRestaurantAsync(_restaurantId, Arg.Any<CancellationToken>())
            .Returns(HubResult<RestaurantSnapshotDto>.Ok(BuildSnapshot()));

        _session = new EngineSession(_engine, _store);
    }

    [Fact]
    public async Task ClustersMerged_UpdatesTheStoreAndLogsTheTablesAsync()
    {
        await _session.StartAsync("default_diner");

        _engine.ClustersMerged += Raise.Event<Action<ClustersMergedDto>>(new ClustersMergedDto(
            3, [3, 4], [3, 4], 4, [new TableMoveDto(4, new GridPointDto(2, 2), new GridPointDto(1, 2))], 2, 701));

        _store.Snapshot!.Floor.Clusters.Single().TableIds.Should().Equal(3, 4);
        _store.Motions.Should().ContainKey(4);
        _session.Log.Last().Should().Contain("T3 + T4").And.Contain("4 seats");
    }

    [Fact]
    public async Task ClusterSplit_UpdatesTheStoreAndLogsItAsync()
    {
        await _session.StartAsync("default_diner");
        _engine.ClustersMerged += Raise.Event<Action<ClustersMergedDto>>(new ClustersMergedDto(3, [3, 4], [3, 4], 4, [], 0, 701));

        _engine.ClusterSplit += Raise.Event<Action<ClusterSplitDto>>(new ClusterSplitDto(
            3, [new ClusterRestoredDto(3, [3], 2), new ClusterRestoredDto(4, [4], 2)], [], 0, 800));

        _store.Snapshot!.Floor.Clusters.Select(c => c.ClusterId).Should().Equal(3, 4);
        _session.Log.Last().Should().Contain("T3 + T4").And.Contain("apart");
    }

    [Fact]
    public async Task TableMoveCompleted_ClearsTheMovingStateWithoutALogLineAsync()
    {
        await _session.StartAsync("default_diner");
        _engine.ClustersMerged += Raise.Event<Action<ClustersMergedDto>>(new ClustersMergedDto(
            3, [3, 4], [3, 4], 4, [new TableMoveDto(4, new GridPointDto(2, 2), new GridPointDto(1, 2))], 2, 701));
        var logLines = _session.Log.Count;

        _engine.TableMoveCompleted += Raise.Event<Action<TableMoveCompletedDto>>(new TableMoveCompletedDto(3, 703));

        _store.Snapshot!.Floor.Clusters.Single().State.Should().Be("Free");
        _store.Motions.Should().BeEmpty();
        _session.Log.Count.Should().Be(logLines);
    }

    [Theory]
    [InlineData(TurnedAwayReasonNames.QueueFull, "queue full")]
    [InlineData(TurnedAwayReasonNames.PartyTooLarge, "party too large")]
    public async Task GroupTurnedAway_LogsWhyAsync(string reason, string expected)
    {
        await _session.StartAsync("default_diner");

        _engine.GroupTurnedAway += Raise.Event<Action<GroupTurnedAwayDto>>(new GroupTurnedAwayDto(19.8f, 700, reason));

        _session.Log.Last().Should().Contain(expected);
    }

    // Two standalone 2-tops (clusters 3 and 4) a cell apart.
    private static RestaurantSnapshotDto BuildSnapshot() => new(
        Guid.NewGuid(), 690, 1, 1, 0, 0m, 0f,
        new FloorDto(
            [
                new TableDto(3, new GridPointDto(0, 2), new GridSizeDto(1, 1), 3, true, false),
                new TableDto(4, new GridPointDto(2, 2), new GridSizeDto(1, 1), 4, true, false)
            ],
            [new ClusterDto(3, [3], 2, "Free", null), new ClusterDto(4, [4], 2, "Free", null)],
            [], [], [], []),
        [], [], []);
}
