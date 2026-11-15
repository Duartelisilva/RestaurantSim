using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceTableMoves;
using RestaurantSim.Application.Tests.Support;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceTableMoves;

public class AdvanceTableMovesCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly AdvanceTableMovesCommandHandler _handler;

    public AdvanceTableMovesCommandHandlerTests()
    {
        _handler = new AdvanceTableMovesCommandHandler(_repository, _publisher);
    }

    [Fact]
    public async Task NothingMoving_DoesNothingAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);

        var result = await RunAsync(restaurant);

        result.IsSuccess.Should().BeTrue();
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(TableMoveCompleted)!, default);
    }

    [Fact]
    public async Task ATableThatHasArrived_StopsMovingAndItsClusterIsAnnouncedAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);
        restaurant.Floor.Tables[1].StartMove(new GridPosition(1, 0), endTick: 100);

        await RunAsync(restaurant);

        var table = restaurant.Floor.Tables[1];
        table.IsMoving.Should().BeFalse();
        table.MoveFrom.Should().BeNull();
        table.Position.Should().Be(new GridPosition(1, 0));
        await _repository.Received(1).SaveAsync(restaurant, Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(
            Arg.Is<TableMoveCompleted>(e => e.ClusterId == 2 && e.Tick == 100 && e.RestaurantId == restaurant.RestaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATableStillSliding_IsLeftAloneAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);
        restaurant.Floor.Tables[1].StartMove(new GridPosition(1, 0), endTick: 101);

        await RunAsync(restaurant);

        restaurant.Floor.Tables[1].IsMoving.Should().BeTrue();
        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(TableMoveCompleted)!, default);
    }

    [Fact]
    public async Task SeveralClustersArriving_EachGetsOneEvent_InClusterOrderAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2, 4);
        restaurant.Floor.Tables[2].StartMove(new GridPosition(3, 0), endTick: 99);
        restaurant.Floor.Tables[0].StartMove(new GridPosition(1, 0), endTick: 100);
        var published = new List<int>();
        _publisher.Publish(Arg.Any<TableMoveCompleted>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            published.Add(call.Arg<TableMoveCompleted>().ClusterId);
            return Task.CompletedTask;
        });

        await RunAsync(restaurant);

        published.Should().Equal(1, 3);
    }

    [Fact]
    public async Task UnknownRestaurant_IsAFailureAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new AdvanceTableMovesCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private async Task<RestaurantSim.Application.Common.Result> RunAsync(Restaurant restaurant)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        return await _handler.Handle(new AdvanceTableMovesCommand(restaurant.RestaurantId), CancellationToken.None);
    }
}
