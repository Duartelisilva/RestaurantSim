using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.EventHandlers;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using Xunit;

namespace RestaurantSim.Application.Tests.EventHandlers;

public class BillPaidStatsHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly BillPaidStatsHandler _handler;

    public BillPaidStatsHandlerTests()
    {
        _handler = new BillPaidStatsHandler(_repository);
    }

    [Fact]
    public async Task Handle_AddsRevenue_ToDailyStatsAsync()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new BillPaid(restaurant.RestaurantId, GroupId: 1, OrderId: 1, Revenue: 25.50m, TipAmount: 5m, CashAfter: 0m, Tick: 100), CancellationToken.None);

        restaurant.DailyStats.Revenue.Should().Be(25.50m);
        await _repository.Received(1).SaveAsync(restaurant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AccumulatesAcrossMultipleBillsAsync()
    {
        var restaurant = BuildRestaurant();
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);

        await _handler.Handle(new BillPaid(restaurant.RestaurantId, 1, 1, 25.50m, 5m, 0m, 100), CancellationToken.None);
        await _handler.Handle(new BillPaid(restaurant.RestaurantId, 2, 2, 40m, 8m, 0m, 150), CancellationToken.None);

        restaurant.DailyStats.Revenue.Should().Be(65.50m);
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenRestaurantDoesNotExistAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var act = async () => await _handler.Handle(new BillPaid(Guid.NewGuid(), 1, 1, 25m, 5m, 0m, 100), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
