using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Domain.CustomerService;

// Pure derivation of a group's DiningStage from state the dining flow already keeps
// (mechanics doc §17, draft). Used by the snapshot and by AdvanceDiningCommandHandler's
// change detection, so both always agree. Not stored on the group — same principle as
// TableCluster.State / IsMerged (no second source of truth).
//
// Precedence matters: PhaseWaitState.Serving wins over NextVisit == Bill, because
// NextVisit is already set to Bill while the dessert is still being cooked and eaten.
public static class DiningStageCalculator
{
    // Looks the group's current order up on the restaurant — the one place that knows how.
    public static DiningStage Calculate(Restaurant restaurant, CustomerGroup group)
    {
        var order = group.CurrentOrderId is int orderId
            ? restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == orderId)
            : null;

        return Calculate(group, order);
    }

    public static DiningStage Calculate(CustomerGroup group, Order? order)
    {
        if (group.LifecycleState == GroupLifecycleState.Leaving)
            return DiningStage.Leaving;

        if (group.PhaseWaitState != GroupPhaseWaitState.Serving)
        {
            // AutoVisitPending, RecheckScheduled (and AwaitingDecision, never stored).
            return group.NextVisit == DiningVisit.Bill ? DiningStage.Paying : DiningStage.Ordering;
        }

        var hasUnservedItems = order is not null && order.Items.Any(i => i.Status != OrderItemStatus.Served);
        return hasUnservedItems ? DiningStage.WaitingForFood : DiningStage.Eating;
    }
}
