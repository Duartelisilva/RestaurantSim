using System.Collections.Concurrent;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Infrastructure.Persistence;

public sealed class InMemoryRestaurantRepository : IRestaurantRepository
{
    private readonly ConcurrentDictionary<Guid, Restaurant> _restaurants = new();

    public Task<Restaurant?> GetAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        _restaurants.TryGetValue(restaurantId, out var restaurant);
        return Task.FromResult(restaurant);
    }

    public Task SaveAsync(Restaurant restaurant, CancellationToken cancellationToken = default)
    {
        _restaurants[restaurant.RestaurantId] = restaurant;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Restaurant>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Restaurant>>(_restaurants.Values.ToList());
}
