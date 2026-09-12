using RestaurantSim.Domain.Statistics;

namespace RestaurantSim.Domain.Abstractions;

// Architecture doc §7.5. StatisticsSnapshot itself carries no RestaurantId
// field (entity doc §23's locked shape is left untouched) — the store
// manages the restaurant association externally, same treatment
// IRestaurantRepository gives Restaurant.
public interface ISnapshotStore
{
    Task AppendAsync(Guid restaurantId, StatisticsSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StatisticsSnapshot>> QueryAsync(Guid restaurantId, CancellationToken cancellationToken = default);
}
