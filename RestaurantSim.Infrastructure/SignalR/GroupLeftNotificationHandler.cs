using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class GroupLeftNotificationHandler : INotificationHandler<GroupLeft>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public GroupLeftNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(GroupLeft notification, CancellationToken cancellationToken)
    {
        var dto = new GroupLeftDto(notification.GroupId, notification.ClusterId, notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.GroupLeft, dto, cancellationToken);
    }
}
