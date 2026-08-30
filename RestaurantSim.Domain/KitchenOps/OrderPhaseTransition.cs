using RestaurantSim.Domain.CustomerService;

namespace RestaurantSim.Domain.KitchenOps;

// Single source of truth for phase-gating (business logic §1.4) — called by
// BOTH PlaceOrderCommandValidator and PlaceOrderCommandHandler. Extracted
// specifically to avoid two independent implementations of the same rule
// drifting apart if one gets edited and not the other.
public static class OrderPhaseTransition
{
    public static bool IsValid(CustomerGroup group, Order? currentOrder, ItemPhase phase)
    {
        return phase switch
        {
            // Beverages can be requested any time before the bill (business logic §1.4/§1.55).
            ItemPhase.Drinks =>
                group.CoursePhase is CoursePhase.Seated or CoursePhase.Drinks or CoursePhase.EntryMain or CoursePhase.Dessert,

            // EntryMain/Dessert each gate on the fixed sequence and can only be
            // placed once per group.
            ItemPhase.EntryMain =>
                group.CoursePhase == CoursePhase.Drinks && !HasExistingItemsForPhase(currentOrder, ItemPhase.EntryMain),

            ItemPhase.Dessert =>
                group.CoursePhase == CoursePhase.EntryMain && !HasExistingItemsForPhase(currentOrder, ItemPhase.Dessert),

            _ => false
        };
    }

    private static bool HasExistingItemsForPhase(Order? order, ItemPhase phase) =>
        order?.Items.Any(i => i.Phase == phase) ?? false;
}
