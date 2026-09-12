using System.Collections.Concurrent;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Statistics;

namespace RestaurantSim.Infrastructure.Persistence;

// MVP implementation — same tier as InMemoryRestaurantRepository (architecture
// doc §7.5). Real persistence is an Infrastructure-only swap later.
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<Guid, List<StatisticsSnapshot>> _snapshotsByRestaurant = new();

    public Task AppendAsync(Guid restaurantId, StatisticsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var list = _snapshotsByRestaurant.GetOrAdd(restaurantId, _ => new List<StatisticsSnapshot>());
        lock (list)
        {
            list.Add(snapshot);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StatisticsSnapshot>> QueryAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var list = _snapshotsByRestaurant.GetOrAdd(restaurantId, _ => new List<StatisticsSnapshot>());
        List<StatisticsSnapshot> copy;
        lock (list)
        {
            copy = list.ToList();
        }
        return Task.FromResult<IReadOnlyList<StatisticsSnapshot>>(copy);
    }
}
