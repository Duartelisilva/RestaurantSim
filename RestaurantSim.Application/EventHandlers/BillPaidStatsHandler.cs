using MediatR;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Application.EventHandlers;

// Accumulates into Restaurant.DailyStats — safe under the same lock the
// originating command (PayBillCommand) already holds while publishing, since
// MediatR's default Publish is awaited sequentially before the command
// handler returns and RestaurantLockBehavior releases the lock.
public sealed class BillPaidStatsHandler : INotificationHandler<BillPaid>
{
    private readonly IRestaurantRepository _repository;

    public BillPaidStatsHandler(IRestaurantRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(BillPaid notification, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(notification.RestaurantId, cancellationToken);
        if (restaurant is null) return; // defensive — shouldn't happen

        restaurant.DailyStats.Revenue += notification.Revenue;

        await _repository.SaveAsync(restaurant, cancellationToken);
    }
}
