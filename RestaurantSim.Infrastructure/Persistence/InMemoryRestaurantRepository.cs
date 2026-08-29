using System.Collections.Concurrent;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Infrastructure.Persistence;

// MVP implementation — session = process lifetime, no save/load (architecture
// doc §4/§6.2). Does not need to be thread-safe on its own merit: every
// Get/Save round-trip already happens inside the per-restaurant lock
// (RestaurantLockBehavior, architecture doc §11), so only one caller is ever
// inside a locked section for a given restaurant at a time. ConcurrentDictionary
// is used anyway as a cheap extra safety margin, not a load-bearing guarantee.
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
}
