using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Domain.CustomerService;

public sealed record OrderChoice(int CustomerId, int MenuItemId);

public sealed record OrderContext(
    IReadOnlyList<MenuItem> Menu,
    IReadOnlyCollection<int> OfferedMenuItemIds,
    string MealPeriodId,
    ServiceFlowConfig Flow);

// Decides WHAT each member orders (mechanics doc §17, draft). Pure: all randomness
// comes in through IRandomProvider; the caller turns the choices into a
// PlaceOrderCommand. Members who are away from the table (bathroom) are NOT
// skipped — they are assumed to have decided before leaving.
public static class OrderGenerator
{
    // Drinks and EntryMain: exactly one item per member. Dessert: each member rolls
    // DessertOrderChance, then orders at most one dessert.
    public static IReadOnlyList<OrderChoice> Generate(
        ItemPhase phase, IEnumerable<Customer> members, OrderContext context, IRandomProvider random)
    {
        var choices = new List<OrderChoice>();

        foreach (var member in members)
        {
            if (phase == ItemPhase.Dessert && !Roll(context.Flow.GetDessertChance(member.SpendingTendency), random))
                continue;

            AddPick(choices, member, phase, context, random);
        }

        return choices;
    }

    // Mid-meal refill: only the members the caller says have finished their drink are
    // passed in; each rolls DrinkRefillChance for at most one more drink.
    public static IReadOnlyList<OrderChoice> GenerateRefills(
        IEnumerable<Customer> eligibleMembers, OrderContext context, IRandomProvider random)
    {
        var choices = new List<OrderChoice>();

        foreach (var member in eligibleMembers)
        {
            if (!Roll(context.Flow.GetDrinkRefillChance(member.SpendingTendency), random))
                continue;

            AddPick(choices, member, ItemPhase.Drinks, context, random);
        }

        return choices;
    }

    private static void AddPick(
        List<OrderChoice> choices, Customer member, ItemPhase phase, OrderContext context, IRandomProvider random)
    {
        // Eligibility is the SAME rule PlaceOrderItemValidation enforces. On top of
        // it, adults pick from the adult menu (the rule alone would allow kids' items).
        var candidates = context.Menu
            .Where(item => context.OfferedMenuItemIds.Contains(item.MenuItemId))
            .Where(item => MenuItemEligibility.IsOrderable(item, phase, member.IsAdult, context.MealPeriodId))
            .Where(item => !member.IsAdult || item.Category == MenuItemCategory.Adult)
            .ToList();

        var picked = PickWeighted(candidates, context.Flow.GetPriceBias(member.SpendingTendency), random);
        if (picked is not null)
            choices.Add(new OrderChoice(member.CustomerId, picked.MenuItemId));
    }

    // Weight = DefaultPrice ^ bias. One NextDouble() call per pick.
    internal static MenuItem? PickWeighted(IReadOnlyList<MenuItem> candidates, float priceBias, IRandomProvider random)
    {
        if (candidates.Count == 0)
            return null;

        var weights = candidates.Select(item => Math.Pow((double)item.DefaultPrice, priceBias)).ToList();
        var remaining = random.NextDouble() * weights.Sum();

        for (var i = 0; i < candidates.Count; i++)
        {
            remaining -= weights[i];
            if (remaining < 0)
                return candidates[i];
        }

        return candidates[^1];
    }

    // Only consumes a random number when the outcome is genuinely uncertain.
    private static bool Roll(float chance, IRandomProvider random)
    {
        if (chance <= 0f) return false;
        if (chance >= 1f) return true;
        return random.NextDouble() < chance;
    }
}
