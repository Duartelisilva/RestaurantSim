using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class ClusterSplitNotificationHandler : INotificationHandler<ClusterSplit>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public ClusterSplitNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(ClusterSplit notification, CancellationToken cancellationToken)
    {
        var dto = new ClusterSplitDto(
            notification.MergedClusterId,
            notification.Restored.Select(r => new ClusterRestoredDto(r.ClusterId, r.TableIds, r.Capacity)).ToList(),
            notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.ClusterSplit, dto, cancellationToken);
    }
}
