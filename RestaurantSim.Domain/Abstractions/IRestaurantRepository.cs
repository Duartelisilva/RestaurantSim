namespace RestaurantSim.Domain.Abstractions;

public interface IRestaurantRepository
{
    Task<Restaurant?> GetAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task SaveAsync(Restaurant restaurant, CancellationToken cancellationToken = default);

    // New — needed by SimulationTickService to iterate every active restaurant
    // each tick (multi-tenant model, entity doc §19). Not needed by any
    // single-restaurant command, only by the tick loop itself.
    Task<IReadOnlyList<Restaurant>> GetAllAsync(CancellationToken cancellationToken = default);
}
