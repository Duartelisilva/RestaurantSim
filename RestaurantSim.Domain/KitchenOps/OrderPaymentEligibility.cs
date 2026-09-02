using RestaurantSim.Domain.CustomerService;

namespace RestaurantSim.Domain.KitchenOps;

// Single source of truth for "can this group pay their bill right now" —
// called by both PayBillCommandValidator and PayBillCommandHandler.
public static class OrderPaymentEligibility
{
    public static bool IsEligible(CustomerGroup group, Order? order)
    {
        // Business logic §1.4's fixed sequence implies at least a main course
        // was ordered before the bill; Dessert is optional (a table can skip
        // straight to Bill after EntryMain, matching real restaurant behavior).
        if (group.CoursePhase is not (CoursePhase.EntryMain or CoursePhase.Dessert))
            return false;

        if (order is null || order.Items.Count == 0)
            return false;

        // Every item (station-cooked or pickup) must actually be Served —
        // you can't pay before your food/drinks have arrived.
        return order.Items.All(i => i.Status == OrderItemStatus.Served);
    }
}
