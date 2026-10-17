using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Infrastructure.SignalR;

// Translates the Domain event into its Contracts DTO and pushes it to the
// per-restaurant SignalR group (architecture doc §3).
public sealed class BillPaidNotificationHandler : INotificationHandler<BillPaid>
{
    private readonly IHubContext<RestaurantHub> _hubContext;

    public BillPaidNotificationHandler(IHubContext<RestaurantHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task Handle(BillPaid notification, CancellationToken cancellationToken)
    {
        var dto = new BillPaidDto(notification.GroupId, notification.Revenue, notification.TipAmount, notification.CashAfter, notification.Tick);

        return _hubContext.Clients
            .Group(notification.RestaurantId.ToString())
            .SendAsync(ClientEventNames.BillPaid, dto, cancellationToken);
    }
}
