using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class GroupTurnedAwayNotificationHandler : INotificationHandler<GroupTurnedAway>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public GroupTurnedAwayNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(GroupTurnedAway notification, CancellationToken cancellationToken)
    {
        var dto = new GroupTurnedAwayDto(notification.ReputationAfter, notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.GroupTurnedAway, dto, cancellationToken);
    }
}
