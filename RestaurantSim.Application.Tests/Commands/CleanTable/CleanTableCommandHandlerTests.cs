using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.CleanTable;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.CleanTable;

public class CleanTableCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly CleanTableCommandHandler _handler;

    public CleanTableCommandHandlerTests()
    {
        _handler = new CleanTableCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_AssignsBusser_ToDirtyTable()
    {
        var (restaurant, busser, table) = BuildScenario(tableIsClean: false, busserState: StaffState.Idle);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new CleanTableCommand(restaurant.RestaurantId, busser.StaffId, table.TableId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        busser.State.Should().Be(StaffState.Busy);
        busser.CurrentTaskType.Should().Be(TaskType.CleanTable);
        busser.CurrentTaskTargetId.Should().Be(table.TableId);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenTableIsAlreadyClean()
    {
        var (restaurant, busser, table) = BuildScenario(tableIsClean: true, busserState: StaffState.Idle);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new CleanTableCommand(restaurant.RestaurantId, busser.StaffId, table.TableId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenBusserIsNotIdle()
    {
        var (restaurant, busser, table) = BuildScenario(tableIsClean: false, busserState: StaffState.Busy);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new CleanTableCommand(restaurant.RestaurantId, busser.StaffId, table.TableId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static (Restaurant Restaurant, StaffMember Busser, Table Table) BuildScenario(bool tableIsClean, StaffState busserState)
    {
        var table = new Table
        {
            TableId = 1, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1),
            Shape = new TableShape { ShapeId = "standard_2top", SeatDefinitions = Array.Empty<SeatDefinition>(), CanMerge = false },
            Seats = Array.Empty<Seat>(), IsClean = tableIsClean
        };

        var busser = new StaffMember
        {
            StaffId = 1, Name = "Busser A", Role = StaffRole.Busser, State = busserState,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };
        restaurant.Floor.Tables.Add(table);
        restaurant.StaffRoster.Staff.Add(busser);

        return (restaurant, busser, table);
    }
}
