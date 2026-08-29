using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Infrastructure-layer translation: Domain raises "this happened," Infrastructure
// decides whether/how that becomes a network message (architecture doc §3).
// Broadcasts to a per-restaurant SignalR group, not all connected clients.
public sealed class GroupSeatedNotificationHandler : INotificationHandler<GroupSeated>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public GroupSeatedNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(GroupSeated notification, CancellationToken cancellationToken)
    {
        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync("GroupSeated", notification, cancellationToken);
    }
}
