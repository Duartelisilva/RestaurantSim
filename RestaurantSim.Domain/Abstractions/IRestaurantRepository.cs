namespace RestaurantSim.Domain.Abstractions;

// The aggregate's own persistence contract — lives in Domain, not Application
// (architecture doc §4). MediatR handlers depend on this, never on concrete
// storage, so swapping to real persistence later is Infrastructure-only.
public interface IRestaurantRepository
{
    // Null return = no restaurant with that Id exists (not an exceptional case).
    Task<Restaurant?> GetAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task SaveAsync(Restaurant restaurant, CancellationToken cancellationToken = default);
}
