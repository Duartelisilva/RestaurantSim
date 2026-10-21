namespace RestaurantSim.Domain.KitchenOps;

// Single source of truth for "can this person order this item right now": the
// course matches, kids only get the kids menu (entity doc §13), and the item is
// available in the active meal period (entity doc §18/§24a). Used by BOTH
// PlaceOrderItemValidation (the rule) and OrderGenerator (candidate selection),
// so the two can never drift apart (CLAUDE.md: no duplicated validator/handler logic).
public static class MenuItemEligibility
{
    public static bool IsOrderable(MenuItem item, ItemPhase phase, bool customerIsAdult, string mealPeriodId)
    {
        return item.Course == phase
            && (customerIsAdult || item.Category == MenuItemCategory.Kids)
            && item.AvailableMealPeriods.Contains(mealPeriodId);
    }
}
