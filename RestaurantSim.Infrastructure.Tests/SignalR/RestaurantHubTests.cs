using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using RestaurantSim.Application.Commands.CreateRestaurant;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.Queries.GetRestaurantSnapshot;
using RestaurantSim.Contracts.Hubs;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Infrastructure.SignalR;

namespace RestaurantSim.Infrastructure.Tests.SignalR;

public sealed class RestaurantHubTests
{
    private const string ConnectionId = "conn-1";

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
    private readonly RestaurantHub _hub;

    public RestaurantHubTests()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(ConnectionId);
        context.ConnectionAborted.Returns(CancellationToken.None);

        _hub = new RestaurantHub(_sender) { Context = context, Groups = _groups };
    }

    [Fact]
    public async Task CreateRestaurantAsync_ReturnsId_WhenCommandSucceeds()
    {
        var id = Guid.NewGuid();
        _sender.Send(Arg.Any<CreateRestaurantCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Success(id));

        var result = await _hub.CreateRestaurantAsync("default_diner");

        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be(id);
    }

    [Fact]
    public async Task CreateRestaurantAsync_ReturnsError_WhenCommandFails()
    {
        _sender.Send(Arg.Any<CreateRestaurantCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Failure("unknown config"));

        var result = await _hub.CreateRestaurantAsync("nope");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("unknown config");
    }

    [Fact]
    public async Task JoinRestaurantAsync_AddsToGroupAndReturnsSnapshot_WhenRestaurantExists()
    {
        var id = Guid.NewGuid();
        var snapshot = EmptySnapshot(id);
        _sender.Send(Arg.Any<GetRestaurantSnapshotQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<RestaurantSnapshotDto>.Success(snapshot));

        var result = await _hub.JoinRestaurantAsync(id);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().BeSameAs(snapshot);
        await _groups.Received(1).AddToGroupAsync(ConnectionId, id.ToString(), Arg.Any<CancellationToken>());
        await _groups.DidNotReceive().RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinRestaurantAsync_LeavesGroupAndReturnsError_WhenRestaurantMissing()
    {
        var id = Guid.NewGuid();
        _sender.Send(Arg.Any<GetRestaurantSnapshotQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<RestaurantSnapshotDto>.Failure("missing"));

        var result = await _hub.JoinRestaurantAsync(id);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("missing");
        await _groups.Received(1).RemoveFromGroupAsync(ConnectionId, id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveRestaurantAsync_RemovesFromGroup()
    {
        var id = Guid.NewGuid();

        await _hub.LeaveRestaurantAsync(id);

        await _groups.Received(1).RemoveFromGroupAsync(ConnectionId, id.ToString(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(RestaurantHubMethods.CreateRestaurant)]
    [InlineData(RestaurantHubMethods.JoinRestaurant)]
    [InlineData(RestaurantHubMethods.LeaveRestaurant)]
    public void MethodNameConstants_MatchRealHubMethods(string methodName)
    {
        typeof(RestaurantHub).GetMethod(methodName).Should().NotBeNull();
    }

    private static RestaurantSnapshotDto EmptySnapshot(Guid id) => new(
        id, 0, 0, 0m, 0f,
        new FloorDto([], [], [], [], [], []),
        [], [], []);
}
