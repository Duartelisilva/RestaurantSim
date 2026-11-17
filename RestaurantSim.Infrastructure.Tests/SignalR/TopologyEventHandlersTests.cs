using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Infrastructure.SignalR;

namespace RestaurantSim.Infrastructure.Tests.SignalR;

// The DTOs carry lists, and records compare lists by reference, so these tests look at the
// sent DTO's content instead of comparing whole records.
public sealed class TopologyEventHandlersTests
{
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly IHubContext<RestaurantHub> _hubContext = Substitute.For<IHubContext<RestaurantHub>>();
    private readonly IClientProxy _proxy = Substitute.For<IClientProxy>();

    public TopologyEventHandlersTests()
    {
        var clients = Substitute.For<IHubClients>();
        _hubContext.Clients.Returns(clients);
        clients.Group(_restaurantId.ToString()).Returns(_proxy);
    }

    [Fact]
    public async Task ClustersMerged_SendsTheResultingTopologyAndTheMovesAsync()
    {
        var handler = new ClustersMergedNotificationHandler(_hubContext);
        var moves = new[] { new TableMove(4, new GridPosition(2, 2), new GridPosition(1, 2)) };

        await handler.Handle(new ClustersMerged(_restaurantId, 3, [3, 4], [3, 4], 4, moves, 2, 120), CancellationToken.None);

        await _proxy.Received(1).SendCoreAsync(
            ClientEventNames.ClustersMerged,
            Arg.Is<object?[]>(args => args.Length == 1
                && args[0] is ClustersMergedDto
                && ((ClustersMergedDto)args[0]!).ClusterId == 3
                && ((ClustersMergedDto)args[0]!).MergedClusterIds.SequenceEqual(new[] { 3, 4 })
                && ((ClustersMergedDto)args[0]!).TableIds.SequenceEqual(new[] { 3, 4 })
                && ((ClustersMergedDto)args[0]!).Capacity == 4
                && ((ClustersMergedDto)args[0]!).DurationTicks == 2
                && ((ClustersMergedDto)args[0]!).Tick == 120
                && ((ClustersMergedDto)args[0]!).Moves.Count == 1
                && ((ClustersMergedDto)args[0]!).Moves[0].TableId == 4
                && ((ClustersMergedDto)args[0]!).Moves[0].From.X == 2 && ((ClustersMergedDto)args[0]!).Moves[0].From.Y == 2
                && ((ClustersMergedDto)args[0]!).Moves[0].To.X == 1 && ((ClustersMergedDto)args[0]!).Moves[0].To.Y == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClustersMerged_WithNoMoves_SendsAnEmptyListAsync()
    {
        var handler = new ClustersMergedNotificationHandler(_hubContext);

        await handler.Handle(new ClustersMerged(_restaurantId, 3, [3, 4], [3, 4], 4, [], 0, 120), CancellationToken.None);

        await _proxy.Received(1).SendCoreAsync(
            ClientEventNames.ClustersMerged,
            Arg.Is<object?[]>(args => ((ClustersMergedDto)args[0]!).Moves.Count == 0 && ((ClustersMergedDto)args[0]!).DurationTicks == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClusterSplit_SendsTheRestoredClustersAndTheMovesAsync()
    {
        var handler = new ClusterSplitNotificationHandler(_hubContext);
        var restored = new List<ClusterRestored> { new(3, [3], 2), new(4, [4], 2) };
        var moves = new[] { new TableMove(4, new GridPosition(1, 2), new GridPosition(2, 2)) };

        await handler.Handle(new ClusterSplit(_restaurantId, 3, restored, moves, 2, 300), CancellationToken.None);

        await _proxy.Received(1).SendCoreAsync(
            ClientEventNames.ClusterSplit,
            Arg.Is<object?[]>(args => args.Length == 1
                && args[0] is ClusterSplitDto
                && ((ClusterSplitDto)args[0]!).MergedClusterId == 3
                && ((ClusterSplitDto)args[0]!).Tick == 300
                && ((ClusterSplitDto)args[0]!).DurationTicks == 2
                && ((ClusterSplitDto)args[0]!).Restored.Count == 2
                && ((ClusterSplitDto)args[0]!).Restored[0].ClusterId == 3
                && ((ClusterSplitDto)args[0]!).Restored[0].TableIds.SequenceEqual(new[] { 3 })
                && ((ClusterSplitDto)args[0]!).Restored[1].ClusterId == 4
                && ((ClusterSplitDto)args[0]!).Restored[1].Capacity == 2
                && ((ClusterSplitDto)args[0]!).Moves.Count == 1
                && ((ClusterSplitDto)args[0]!).Moves[0].TableId == 4
                && ((ClusterSplitDto)args[0]!).Moves[0].To.X == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TableMoveCompleted_SendsTheClusterThatIsUsableAgainAsync()
    {
        var handler = new TableMoveCompletedNotificationHandler(_hubContext);

        await handler.Handle(new TableMoveCompleted(_restaurantId, 3, 122), CancellationToken.None);

        var expected = new TableMoveCompletedDto(3, 122);
        await _proxy.Received(1).SendCoreAsync(
            ClientEventNames.TableMoveCompleted,
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], expected)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TurnedAwayReason.QueueFull, TurnedAwayReasonNames.QueueFull)]
    [InlineData(TurnedAwayReason.PartyTooLarge, TurnedAwayReasonNames.PartyTooLarge)]
    public async Task GroupTurnedAway_SendsTheReasonAsync(TurnedAwayReason reason, string wireName)
    {
        var handler = new GroupTurnedAwayNotificationHandler(_hubContext);

        await handler.Handle(new GroupTurnedAway(_restaurantId, 19.8f, 55, reason), CancellationToken.None);

        var expected = new GroupTurnedAwayDto(19.8f, 55, wireName);
        await _proxy.Received(1).SendCoreAsync(
            ClientEventNames.GroupTurnedAway,
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], expected)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TurnedAwayReasonNames_MatchTheDomainEnumMembers()
    {
        string[] wireNames = [TurnedAwayReasonNames.QueueFull, TurnedAwayReasonNames.PartyTooLarge];

        wireNames.Should().BeEquivalentTo(Enum.GetNames<TurnedAwayReason>());
    }
}
