using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.TickProcessing;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.TickProcessing;

public class RestaurantTickProcessorSingleRestaurantTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IAutoManagerRunner _autoManager = Substitute.For<IAutoManagerRunner>();
    private readonly RestaurantTickProcessor _processor;

    public RestaurantTickProcessorSingleRestaurantTests()
    {
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _processor = new RestaurantTickProcessor(
            _repository, _sender, _autoManager, NullLogger<RestaurantTickProcessor>.Instance);
    }

    [Fact]
    public async Task AdvanceRestaurantAsync_TicksOnlyTheGivenRestaurantAsync()
    {
        var a = Build();
        var b = Build();

        await _processor.AdvanceRestaurantAsync(a, CancellationToken.None);

        a.SimulationTick.Should().Be(1);
        b.SimulationTick.Should().Be(0);
        await _repository.Received(1).SaveAsync(a, Arg.Any<CancellationToken>());
        await _autoManager.Received(1).RunPassAsync(a.RestaurantId, Arg.Any<CancellationToken>());
        await _autoManager.DidNotReceive().RunPassAsync(b.RestaurantId, Arg.Any<CancellationToken>());
    }

    private static Restaurant Build() => new()
    {
        RestaurantId = Guid.NewGuid(),
        ConfigId = "default_diner",
        Floor = new FloorState(),
        StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(),
        KitchenOps = new KitchenOpsState(),
        Events = new EventState()
    };
}
