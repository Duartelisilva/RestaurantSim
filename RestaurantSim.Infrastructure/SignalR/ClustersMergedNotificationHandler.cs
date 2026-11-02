using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class ClustersMergedNotificationHandler : INotificationHandler<ClustersMerged>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public ClustersMergedNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(ClustersMerged notification, CancellationToken cancellationToken)
    {
        var dto = new ClustersMergedDto(
            notification.ClusterId, notification.MergedClusterIds, notification.TableIds,
            notification.Capacity, notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.ClustersMerged, dto, cancellationToken);
    }
}
