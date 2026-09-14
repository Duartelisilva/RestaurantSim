using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Common;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using Xunit;

namespace RestaurantSim.AutoManager.Tests;

public class AutoManagerProcessorTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ISeatingStrategy _seatingStrategy = Substitute.For<ISeatingStrategy>();
    private readonly ILogger<AutoManagerProcessor> _logger = Substitute.For<ILogger<AutoManagerProcessor>>();
    private readonly AutoManagerProcessor _processor;

    public AutoManagerProcessorTests()
    {
        _processor = new AutoManagerProcessor(_repository, _sender, _seatingStrategy, _logger);
    }

    [Fact]
    public async Task RunPassAsync_DispatchesSeatGroupCommand_ForEachDecision_UntilStrategyReturnsNull()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _sender.Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _seatingStrategy.DecideNextSeating(restaurant).Returns( ((int GroupId, int ClusterId)?)(1, 10), ((int GroupId, int ClusterId)?)(2, 11), ((int GroupId, int ClusterId)?)null);

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.Received(1).Send(Arg.Is<SeatGroupCommand>(c => c.GroupId == 1 && c.ClusterId == 10), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Is<SeatGroupCommand>(c => c.GroupId == 2 && c.ClusterId == 11), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_DoesNothing_WhenStrategyImmediatelyReturnsNull()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _seatingStrategy.DecideNextSeating(restaurant).Returns((ValueTuple<int, int>?)null);

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await _sender.DidNotReceive().Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_SkipsSeatingEntirely_WhenSeatingToggleIsOff()
    {
        var restaurant = BuildRestaurant();
        restaurant.AutoManagerToggles[AutoManagerSystem.Seating] = false;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        _seatingStrategy.DidNotReceive().DecideNextSeating(Arg.Any<Restaurant>());
        await _sender.DidNotReceive().Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_StopsPass_WhenSeatGroupCommandFails()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _sender.Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure("table no longer free"));
        _seatingStrategy.DecideNextSeating(restaurant).Returns((1, 10), (2, 11));

        await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        // Only the first decision was attempted — pass stopped on failure
        // rather than retrying an invalid decision indefinitely.
        await _sender.Received(1).Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunPassAsync_StopsPass_WhenDispatchThrows()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _sender.Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result>(new InvalidOperationException("boom")));
        _seatingStrategy.DecideNextSeating(restaurant).Returns((1, 10));

        var act = async () => await _processor.RunPassAsync(restaurant.RestaurantId, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunPassAsync_DoesNothing_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var act = async () => await _processor.RunPassAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _sender.DidNotReceive().Send(Arg.Any<SeatGroupCommand>(), Arg.Any<CancellationToken>());
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
