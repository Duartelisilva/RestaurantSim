using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.CleanBathroom;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.CleanBathroom;

public class BussersCleanBathroomCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly BussersCleanBathroomCommandHandler _handler;

    public BussersCleanBathroomCommandHandlerTests()
    {
        _handler = new BussersCleanBathroomCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_AssignsBusser_ToDirtyBathroomAsync()
    {
        var (restaurant, busser, bathroom) = BuildScenario(cleanlinessScore: 20f, maxConcurrentCleaners: 1, assignedCount: 0);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new BussersCleanBathroomCommand(restaurant.RestaurantId, busser.StaffId, bathroom.BathroomId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        busser.State.Should().Be(StaffState.Busy);
        busser.CurrentTaskType.Should().Be(TaskType.CleanBathroom);
        busser.CurrentTaskTargetId.Should().Be(bathroom.BathroomId);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenAlreadyFullyCleanAsync()
    {
        var (restaurant, busser, bathroom) = BuildScenario(cleanlinessScore: 100f, maxConcurrentCleaners: 1, assignedCount: 0);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new BussersCleanBathroomCommand(restaurant.RestaurantId, busser.StaffId, bathroom.BathroomId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenMaxConcurrentCleanersReachedAsync()
    {
        var (restaurant, busser, bathroom) = BuildScenario(cleanlinessScore: 20f, maxConcurrentCleaners: 1, assignedCount: 0);
        var otherBusser = new StaffMember
        {
            StaffId = 2, Name = "Busser B", Role = StaffRole.Busser, State = StaffState.Busy,
            CurrentTaskType = TaskType.CleanBathroom, CurrentTaskTargetId = bathroom.BathroomId,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        };
        restaurant.StaffRoster.Staff.Add(otherBusser);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new BussersCleanBathroomCommand(restaurant.RestaurantId, busser.StaffId, bathroom.BathroomId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static (Restaurant Restaurant, StaffMember Busser, Bathroom Bathroom) BuildScenario(float cleanlinessScore, int maxConcurrentCleaners, int assignedCount)
    {
        var bathroom = new Bathroom
        {
            BathroomId = 1, Gender = BathroomGender.Male, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1),
            Capacity = 1, MaxConcurrentCleaners = maxConcurrentCleaners, CleanlinessScore = cleanlinessScore
        };

        var busser = new StaffMember
        {
            StaffId = 1, Name = "Busser A", Role = StaffRole.Busser, State = StaffState.Idle,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };
        restaurant.Floor.Bathrooms.Add(bathroom);
        restaurant.StaffRoster.Staff.Add(busser);

        return (restaurant, busser, bathroom);
    }
}
