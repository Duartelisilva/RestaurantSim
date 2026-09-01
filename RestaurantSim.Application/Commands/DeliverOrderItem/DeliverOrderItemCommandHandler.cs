using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.DeliverOrderItem;

public sealed class DeliverOrderItemCommandHandler : IRequestHandler<DeliverOrderItemCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPublisher _publisher;

    public DeliverOrderItemCommandHandler(IRestaurantRepository repository, IPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(DeliverOrderItemCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var order = restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.Items.Any(i => i.OrderItemId == request.OrderItemId));
        var item = order?.Items.FirstOrDefault(i => i.OrderItemId == request.OrderItemId);
        if (order is null || item is null)
            return Result.Failure($"OrderItem {request.OrderItemId} does not exist.");

        if (item.Status is not (OrderItemStatus.Ready or OrderItemStatus.HeldUnderLamp))
            return Result.Failure($"OrderItem {request.OrderItemId} is not ready for delivery (current status: {item.Status}).");

        var waiter = restaurant.StaffRoster.Staff.FirstOrDefault(s => s.Role == StaffRole.Waiter && s.State == StaffState.Idle);
        if (waiter is null)
            return Result.Failure("No idle waiter is available to deliver this item.");

        var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == order.GroupId);
        if (group is null)
            return Result.Failure($"Group {order.GroupId} for order {order.OrderId} is not currently active.");

        // Hold clock stops at pickup for delivery, not at arrival at the table
        // (mechanics doc §10). HeldTicks is 0 if the item never went under the
        // lamp (straight Cooking -> Ready), so the formula below applies uniformly.
        long heldTicks = 0;
        if (item.Status == OrderItemStatus.HeldUnderLamp && item.HeldSinceTick is long heldSince)
        {
            heldTicks = restaurant.SimulationTick - heldSince;
            var lamp = restaurant.Floor.HeatLamps.FirstOrDefault(l => l.OccupiedSlots.Contains(item.OrderItemId));
            lamp?.OccupiedSlots.Remove(item.OrderItemId);

            var decayRate = lamp?.QualityDecayRatePerTick ?? 0f;
            var effectiveDecayRate = decayRate * (item.IsHighQuality ? 0.5f : 1.0f);
            var baseQuality = item.IsHighQuality ? 100f : (item.HadFoodQualityEvent ? 30f : 80f);
            var qualityAtServe = Math.Clamp(baseQuality - heldTicks * effectiveDecayRate, 0f, 100f);

            item.Status = OrderItemStatus.Served;
            return await FinalizeDeliveryAsync(restaurant, order, item, group, qualityAtServe, cancellationToken);
        }

        // Never held — quality decay never applied.
        var baseQualityNoHold = item.IsHighQuality ? 100f : (item.HadFoodQualityEvent ? 30f : 80f);
        item.Status = OrderItemStatus.Served;
        return await FinalizeDeliveryAsync(restaurant, order, item, group, baseQualityNoHold, cancellationToken);
    }

    private async Task<Result> FinalizeDeliveryAsync(
        Domain.Restaurant restaurant,
        Order order,
        OrderItem item,
        Domain.CustomerService.CustomerGroup group,
        float qualityAtServe,
        CancellationToken cancellationToken)
    {
        // Mood impact at serve (mechanics doc §10).
        var moodBonus = qualityAtServe switch
        {
            >= 90 => 10f,
            >= 60 => 0f,
            _ => -8f
        };

        // Applied regardless of IsPresentAtTable — this is a discrete event, not
        // decay/recovery/spread, which is what mechanics doc §12's Mood-freeze-
        // while-away rule actually covers. Judgment call, flagged in pending-doc-changes.
        var owner = group.Members.FirstOrDefault(m => m.CustomerId == item.OwnerId);
        if (owner is not null)
        {
            owner.Mood = Math.Clamp(owner.Mood + moodBonus, 0, 100);
        }

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(
            new OrderItemServed(restaurant.RestaurantId, item.OrderItemId, order.GroupId, qualityAtServe, moodBonus, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
