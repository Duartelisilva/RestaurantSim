using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Hubs;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Engine;

// The only door between UI code and the simulation engine. Nothing in the UI
// may assume SignalR, a remote server, or an in-process engine.
public interface IEngineClient : IAsyncDisposable
{
    bool IsConnected { get; }

    event Action<GroupArrivedDto>? GroupArrived;
    event Action<GroupAbandonedQueueDto>? GroupAbandonedQueue;
    event Action<GroupSeatedDto>? GroupSeated;
    event Action<GroupLeftDto>? GroupLeft;
    event Action<TableCleanedDto>? TableCleaned;

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<HubResult<Guid>> CreateRestaurantAsync(string configId, CancellationToken cancellationToken = default);

    Task<HubResult<RestaurantSnapshotDto>> JoinRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task LeaveRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);
}
