using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.EventHandlers;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using Xunit;

namespace RestaurantSim.Application.Tests.EventHandlers;

public class GroupLeftStatsHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly GroupLeftStatsHandler _handler;

    public GroupLeftStatsHandlerTests()
    {
        _handler = new GroupLeftStatsHandler(_repository);
    }

    [Fact]
    public async Task Handle_CountsIndividualPeople_NotVisitsAsync()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new GroupLeft(restaurant.RestaurantId, GroupId: 1, ClusterId: 1, PartySize: 4, FinalAverageMood: 70f, Tick: 100), CancellationToken.None);

        restaurant.DailyStats.CustomersServed.Should().Be(4); // party size, not 1
    }

    [Fact]
    public async Task Handle_WeightsFinalMoodByPartySizeAsync()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        // Party of 4 at mood 80 should count 4x more than a party of 1 at mood 20
        // toward the day's weighted average.
        await _handler.Handle(new GroupLeft(restaurant.RestaurantId, 1, 1, PartySize: 4, FinalAverageMood: 80f, Tick: 100), CancellationToken.None);
        await _handler.Handle(new GroupLeft(restaurant.RestaurantId, 2, 2, PartySize: 1, FinalAverageMood: 20f, Tick: 150), CancellationToken.None);

        restaurant.DailyStats.CustomersServed.Should().Be(5);
        restaurant.DailyStats.WeightedFinalMoodSum.Should().Be(80f * 4 + 20f * 1); // 340

        var avgFinalMood = restaurant.DailyStats.WeightedFinalMoodSum / restaurant.DailyStats.CustomersServed;
        avgFinalMood.Should().Be(68f); // 340/5, correctly weighted toward the larger party
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
