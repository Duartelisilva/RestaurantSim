using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Commands.AdvanceKitchen;
using RestaurantSim.Application.Commands.AdvanceTableMoves;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.Tests.Support;
using RestaurantSim.Application.TickProcessing;
using RestaurantSim.Domain.Abstractions;
using Xunit;

namespace RestaurantSim.Application.Tests.TickProcessing;

// Architecture doc §2a, step 1: scheduled completions resolve first, so table moves that
// arrive this tick are done before anything else looks at the floor.
public class RestaurantTickProcessorTableMovesTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly RestaurantTickProcessor _processor;

    public RestaurantTickProcessorTableMovesTests()
    {
        _processor = new RestaurantTickProcessor(
            _repository, _sender, Substitute.For<IAutoManagerRunner>(), Substitute.For<ILogger<RestaurantTickProcessor>>());
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task EachTick_ResolvesTableMoves_BeforeTheKitchenAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);
        var calls = new List<string>();
        _sender.Send(Arg.Any<AdvanceTableMovesCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("moves");
            return Result.Success();
        });
        _sender.Send(Arg.Any<AdvanceKitchenCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("kitchen");
            return Result.Success();
        });

        await _processor.AdvanceRestaurantAsync(restaurant, CancellationToken.None);

        calls.Should().Equal("moves", "kitchen");
    }
}
