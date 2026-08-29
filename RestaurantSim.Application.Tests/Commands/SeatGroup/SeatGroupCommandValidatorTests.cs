using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SeatGroup;

public class SeatGroupCommandValidatorTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();

    [Fact]
    public async Task Validate_Succeeds_WhenGroupIsQueuedAndClusterFits()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        var validator = new SeatGroupCommandValidator(_repository);

        var result = await validator.ValidateAsync(new SeatGroupCommand(restaurant.RestaurantId, 1, 1));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Fails_WhenGroupIsNotQueued()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        var validator = new SeatGroupCommandValidator(_repository);

        var result = await validator.ValidateAsync(new SeatGroupCommand(restaurant.RestaurantId, 999, 1));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Fails_WhenClusterIsAlreadyOccupied()
    {
        var restaurant = BuildRestaurant();
        restaurant.Floor.Clusters[0].OccupiedBy = 999;
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        var validator = new SeatGroupCommandValidator(_repository);

        var result = await validator.ValidateAsync(new SeatGroupCommand(restaurant.RestaurantId, 1, 1));

        result.IsValid.Should().BeFalse();
    }

    private static Restaurant BuildRestaurant()
    {
        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        restaurant.CustomerService.Queue.Add(new CustomerGroup
        {
            GroupId = 1,
            PartySize = 2,
            Members = Array.Empty<Customer>(),
            ArrivalTick = 0
        });

        restaurant.Floor.Clusters.Add(new TableCluster
        {
            ClusterId = 1,
            TableIds = new List<int> { 1 },
            Capacity = 2,
        });

        return restaurant;
    }
}
