using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceKitchen;
using RestaurantSim.Application.Commands.AdvanceMood;
using RestaurantSim.Application.Commands.AdvanceStaffShift;
using RestaurantSim.Application.Commands.EndDay;
using RestaurantSim.Application.Commands.ProcessQueueAbandonment;
using RestaurantSim.Application.Commands.SpawnArrival;
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
    private readonly ILogger<RestaurantTickProcessor> _logger = Substitute.For<ILogger<RestaurantTickProcessor>>();
    private readonly RestaurantTickProcessor _processor;

    public RestaurantTickProcessorTests()
    {
        _processor = new RestaurantTickProcessor(_repository, _sender, _logger);
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_IncrementsSimulationTick_ForEachRestaurant()
    {
        var restaurant = BuildRestaurant(simulationTick: 100);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurant });

        await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        restaurant.SimulationTick.Should().Be(101);
        await _repository.Received(1).SaveAsync(restaurant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_DispatchesAllSixCommands_PerRestaurant()
    {
        var restaurant = BuildRestaurant(simulationTick: 100);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurant });

        await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await _sender.Received(1).Send(Arg.Any<AdvanceKitchenCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<SpawnArrivalCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<AdvanceMoodCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<AdvanceStaffShiftCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<ProcessQueueAbandonmentCommand>(), Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(Arg.Any<EndDayCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_ProcessesEachRestaurantIndependently()
    {
        var restaurantA = BuildRestaurant(simulationTick: 100);
        var restaurantB = BuildRestaurant(simulationTick: 200);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurantA, restaurantB });

        await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        restaurantA.SimulationTick.Should().Be(101);
        restaurantB.SimulationTick.Should().Be(201);
        await _sender.Received(6).Send(Arg.Is<IRequest<Result>>(r => BelongsTo(r, restaurantA.RestaurantId)), Arg.Any<CancellationToken>());
        await _sender.Received(6).Send(Arg.Is<IRequest<Result>>(r => BelongsTo(r, restaurantB.RestaurantId)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_ContinuesProcessing_WhenACommandReturnsFailure()
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
    public async Task AdvanceAllRestaurantsAsync_ContinuesProcessing_WhenACommandThrows()
    {
        var restaurantA = BuildRestaurant(simulationTick: 100);
        var restaurantB = BuildRestaurant(simulationTick: 200);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant> { restaurantA, restaurantB });
        _sender.Send(Arg.Any<SpawnArrivalCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result>(new InvalidOperationException("boom")));

        var act = async () => await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        restaurantB.SimulationTick.Should().Be(201);
    }

    [Fact]
    public async Task AdvanceAllRestaurantsAsync_DoesNothing_WhenNoRestaurantsExist()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Restaurant>());

        var act = async () => await _processor.AdvanceAllRestaurantsAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _sender.DidNotReceive().Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>());
    }

    private static bool BelongsTo(IRequest<Result> request, Guid restaurantId) => request switch
    {
        AdvanceKitchenCommand c => c.RestaurantId == restaurantId,
        SpawnArrivalCommand c => c.RestaurantId == restaurantId,
        AdvanceMoodCommand c => c.RestaurantId == restaurantId,
        AdvanceStaffShiftCommand c => c.RestaurantId == restaurantId,
        ProcessQueueAbandonmentCommand c => c.RestaurantId == restaurantId,
        EndDayCommand c => c.RestaurantId == restaurantId,
        _ => false
    };

    private static Restaurant BuildRestaurant(long simulationTick) => new()
    {
        RestaurantId = Guid.NewGuid(),
        ConfigId = "default_diner",
        SimulationTick = simulationTick,
        Floor = new FloorState(),
        StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(),
        KitchenOps = new KitchenOpsState(),
        Events = new EventState()
    };
}
