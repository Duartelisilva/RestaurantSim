using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.LeaveTable;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.KitchenOps;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.LeaveTable;

public class LeaveTableCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly LeaveTableCommandHandler _handler;

    public LeaveTableCommandHandlerTests()
    {
        _handler = new LeaveTableCommandHandler(_repository, _publisher);
    }

    [Fact]
    public async Task Handle_RemovesGroup_FreesCluster_AndDirtiesTables()
    {
        var (restaurant, group, cluster, table1, table2) = BuildScenario();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new LeaveTableCommand(restaurant.RestaurantId, group.GroupId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.ActiveGroups.Should().NotContain(group);
        cluster.OccupiedBy.Should().BeNull();
        table1.IsClean.Should().BeFalse();
        table2.IsClean.Should().BeFalse();

        await _publisher.Received(1).Publish(
            Arg.Is<GroupLeft>(e => e.GroupId == group.GroupId && e.ClusterId == cluster.ClusterId && e.PartySize == group.PartySize),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemovesOrder_FromActiveOrders()
    {
        var (restaurant, group, _, _, _) = BuildScenario();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new LeaveTableCommand(restaurant.RestaurantId, group.GroupId), CancellationToken.None);

        restaurant.KitchenOps.ActiveOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenGroupHasNotPaidYet()
    {
        var (restaurant, group, _, _, _) = BuildScenario();
        group.LifecycleState = GroupLifecycleState.Seated;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new LeaveTableCommand(restaurant.RestaurantId, group.GroupId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        restaurant.CustomerService.ActiveGroups.Should().Contain(group);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenGroupIsNotActive()
    {
        var (restaurant, _, _, _, _) = BuildScenario();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        var result = await _handler.Handle(new LeaveTableCommand(restaurant.RestaurantId, GroupId: 999), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new LeaveTableCommand(Guid.NewGuid(), 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static (Restaurant Restaurant, CustomerGroup Group, TableCluster Cluster, Table Table1, Table Table2) BuildScenario()
    {
        var customer = new Customer
        {
            CustomerId = 1, IsAdult = true, Mood = 70, PatienceLevel = 0.5f, Instability = 0.5f,
            SusceptibilityToGroup = 0.5f, SpendingTendency = SpendingTendency.Normal, IsRegular = false, Gender = Gender.Male
        };

        var group = new CustomerGroup
        {
            GroupId = 1, PartySize = 2, Members = new List<Customer> { customer }, ArrivalTick = 0,
            ClusterId = 10, LifecycleState = GroupLifecycleState.Leaving, CoursePhase = CoursePhase.Done,
            CurrentOrderId = 1
        };

        var table1 = new Table
        {
            TableId = 1, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1),
            Shape = new TableShape { ShapeId = "standard_2top", SeatDefinitions = Array.Empty<SeatDefinition>(), CanMerge = false },
            Seats = Array.Empty<Seat>(), IsClean = true, ClusterId = 10
        };

        var table2 = new Table
        {
            TableId = 2, Position = new GridPosition(1, 0), Hitbox = new GridBounds(1, 1),
            Shape = new TableShape { ShapeId = "standard_2top", SeatDefinitions = Array.Empty<SeatDefinition>(), CanMerge = false },
            Seats = Array.Empty<Seat>(), IsClean = true, ClusterId = 10
        };

        var cluster = new TableCluster { ClusterId = 10, TableIds = new List<int> { 1, 2 }, Capacity = 4, OccupiedBy = group.GroupId };

        var order = new Order { OrderId = 1, GroupId = 1, ClusterId = 10, Items = new List<OrderItem>(), CreatedAtTick = 0 };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
            Floor = new FloorState(), StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
        };

        restaurant.Floor.Tables.Add(table1);
        restaurant.Floor.Tables.Add(table2);
        restaurant.Floor.Clusters.Add(cluster);
        restaurant.CustomerService.ActiveGroups.Add(group);
        restaurant.KitchenOps.ActiveOrders.Add(order);

        return (restaurant, group, cluster, table1, table2);
    }
}
