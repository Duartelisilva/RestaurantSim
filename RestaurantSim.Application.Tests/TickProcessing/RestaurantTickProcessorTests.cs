using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Commands.AdvanceCleaningProgress;
using RestaurantSim.Application.Commands.AdvanceKitchen;
using RestaurantSim.Application.Commands.AdvanceMood;
using RestaurantSim.Application.Commands.AdvanceStaffShift;
using RestaurantSim.Application.Commands.EndDay;
using RestaurantSim.Application.Commands.ProcessQueueAbandonment;
using RestaurantSim.Application.Commands.SpawnArrival;
using RestaurantSim.Application.Commands.SpreadGroupMood;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.TickProcessing;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using Xunit;

namespace RestaurantSim.Application.Tests.TickProcessing;

public class RestaurantTickProcessorTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IAutoManagerRunner _autoManagerRunner = Substitute.For<IAutoManagerRunner>();
    private readonly ILogger<RestaurantTickProcessor> _logger = Substitute.For<ILogger<RestaurantTickProcessor>>();
    private readonly RestaurantTickProcessor _processor;

    public RestaurantTickProcessorTests()
    {
        _processor = new RestaurantTickProcessor(_repository, _sender, _autoManagerRunner, _logger);
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_IncrementsSimulationTick_ForEachRestaurantAsync()
    {
        var restaurant = BuildRestaurant(simulationTick: 100);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurant });

        await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        restaurant.SimulationTick.Should().Be(101);
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_DispatchesAllEightCommands_PerRestaurantAsync()
    {
        var restaurant = BuildRestaurant(simulationTick: 100);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurant });

        await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await _sender.Received(1).Send(Arg.Any<AdvanceKitchenCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<SpawnArrivalCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<AdvanceCleaningProgressCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<AdvanceMoodCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<AdvanceStaffShiftCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<SpreadGroupMoodCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<ProcessQueueAbandonmentCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<EndDayCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_RunsAutoManagerPass_PerRestaurantAsync()
    {
        var restaurant = BuildRestaurant(simulationTick: 100);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurant });

        await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await _autoManagerRunner.Received(1).RunPassAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_ContinuesProcessing_WhenACommandReturnsFailureAsync()
    {
        var restaurantA = BuildRestaurant(simulationTick: 100);
        var restaurantB = BuildRestaurant(simulationTick: 200);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurantA, restaurantB });
        _sender.Send(Arg.Any<AdvanceKitchenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure("boom"));

        var act = async () => await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        restaurantB.SimulationTick.Should().Be(201);
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_ContinuesProcessing_WhenAutoManagerThrowsAsync()
    {
        var restaurantA = BuildRestaurant(simulationTick: 100);
        var restaurantB = BuildRestaurant(simulationTick: 200);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurantA, restaurantB });
        _autoManagerRunner.RunPassAsync(restaurantA.RestaurantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = async () => await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        restaurantB.SimulationTick.Should().Be(201);
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_DoesNothing_WhenNoRestaurantsExistAsync()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant>());

        var act = async () => await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _sender.DidNotReceive().Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>());
    }

    private static Restaurant BuildRestaurant(long simulationTick) => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = simulationTick,
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
