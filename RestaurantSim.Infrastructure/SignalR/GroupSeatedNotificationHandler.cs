using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Infrastructure-layer translation: Domain raises "this happened," Infrastructure
// decides whether/how that becomes a network message (architecture doc §3).
// Sends a Contracts DTO (the wire contract), never the Domain notification itself,
// to the per-restaurant SignalR group.
public sealed class GroupSeatedNotificationHandler : INotificationHandler<GroupSeated>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public GroupSeatedNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(GroupSeated notification, CancellationToken cancellationToken)
    {
        var dto = new GroupSeatedDto(notification.GroupId, notification.ClusterId, notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.GroupSeated, dto, cancellationToken);
    }
}
