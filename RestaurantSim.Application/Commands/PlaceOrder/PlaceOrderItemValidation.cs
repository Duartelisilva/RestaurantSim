using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Application.Commands.PlaceOrder;

// Single source of truth for item-level rules (membership, restaurant menu
// subset, kids-menu enforcement, meal-period gating) — called by BOTH
// PlaceOrderCommandValidator and PlaceOrderCommandHandler. Lives in Application
// (not Domain) because it orchestrates async repository calls, which is a
// use-case concern, not a pure domain rule (unlike OrderPhaseTransition).
public static class PlaceOrderItemValidation
{
    public static async Task<bool> AllItemsAreValidAsync(
        Restaurant restaurant,
        RestaurantConfig config,
        CustomerGroup group,
        ItemPhase phase,
        IReadOnlyList<PlaceOrderItemRequest> items,
        IMenuRepository menuRepository,
        CancellationToken cancellationToken)
    {
        // Served-out groups can still order after their period closes (FindServicePeriod).
        var activePeriod = MealPeriodCalculator.FindServicePeriod(config.ActiveMealPeriods, config.MinutesPerTick, restaurant.SimulationTick);
        if (activePeriod is null) return false;

        foreach (var itemRequest in items)
        {
            var customer = group.Members.FirstOrDefault(m => m.CustomerId == itemRequest.CustomerId);
            if (customer is null) return false;

            // Restaurant only serves items on its own menu subset (entity doc §22).
            if (!config.MenuItemIds.Contains(itemRequest.MenuItemId)) return false;

            var menuItem = await menuRepository.GetByIdAsync(itemRequest.MenuItemId, cancellationToken);
            if (menuItem is null) return false;

            // Course match, kids-menu enforcement (entity doc §13) and meal-period
            // restriction (business logic §1.4, entity doc §18/§24a) live in ONE shared
            // predicate, also used by OrderGenerator to choose what to order.
            if (!MenuItemEligibility.IsOrderable(menuItem, phase, customer.IsAdult, activePeriod.MealPeriodId)) return false;
        }

        return true;
    }
}
