using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class GroupAbandonedQueueNotificationHandler : INotificationHandler<GroupAbandonedQueue>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public GroupAbandonedQueueNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(GroupAbandonedQueue notification, CancellationToken cancellationToken)
    {
        var dto = new GroupAbandonedQueueDto(notification.GroupId, notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.GroupAbandonedQueue, dto, cancellationToken);
    }
}
