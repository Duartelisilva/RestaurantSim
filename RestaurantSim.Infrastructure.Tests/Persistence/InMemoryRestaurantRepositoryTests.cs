using FluentAssertions;
using RestaurantSim.Domain;
using RestaurantSim.Infrastructure.Persistence;
using Xunit;

namespace RestaurantSim.Infrastructure.Tests.Persistence;

public class InMemoryRestaurantRepositoryTests
{
    [Fact]
    public async Task GetAsync_ReturnsNull_WhenRestaurantDoesNotExistAsync()
    {
        var repository = new InMemoryRestaurantRepository();

        var result = await repository.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsTheSameRestaurantAsync()
    {
        var repository = new InMemoryRestaurantRepository();
        var restaurant = CreateMinimalRestaurant();

        await repository.SaveAsync(restaurant);
        var result = await repository.GetAsync(restaurant.RestaurantId);

        result.Should().Be(restaurant);
    }

    [Fact]
    public async Task SaveAsync_Overwrites_WhenCalledTwiceForSameIdAsync()
    {
        var repository = new InMemoryRestaurantRepository();
        var restaurant = CreateMinimalRestaurant();
        await repository.SaveAsync(restaurant);

        restaurant.Cash = 9999m;
        await repository.SaveAsync(restaurant);
        var result = await repository.GetAsync(restaurant.RestaurantId);

        result!.Cash.Should().Be(9999m);
    }

    private static Restaurant CreateMinimalRestaurant() => new()
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
