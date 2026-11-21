using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class StaffStateChangedNotificationHandler : INotificationHandler<StaffStateChanged>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public StaffStateChangedNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(StaffStateChanged notification, CancellationToken cancellationToken)
    {
        var dto = new StaffStateChangedDto(
            notification.StaffId, notification.State.ToString(), notification.TaskType?.ToString(),
            notification.TaskTargetId, notification.Fatigue, notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.StaffStateChanged, dto, cancellationToken);
    }
}
