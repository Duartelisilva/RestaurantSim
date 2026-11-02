using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class GroupDiningStageChangedNotificationHandler : INotificationHandler<GroupDiningStageChanged>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public GroupDiningStageChangedNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(GroupDiningStageChanged notification, CancellationToken cancellationToken)
    {
        var dto = new GroupDiningStageChangedDto(
            notification.GroupId, notification.Stage.ToString(), notification.NextVisit.ToString(), notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.GroupDiningStageChanged, dto, cancellationToken);
    }
}
