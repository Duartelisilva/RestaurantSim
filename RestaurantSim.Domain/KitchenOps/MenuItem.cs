namespace RestaurantSim.Domain.KitchenOps;

public enum MenuItemCategory
{
    Adult,
    Kids
}

// Shared by OrderItem.Phase and CustomerGroup.CoursePhase's ordering-relevant subset.
public enum ItemPhase
{
    Drinks,
    EntryMain,
    Dessert
}

// Preset/reference data, shared by pointer across all OrderItems — not owned per-instance.
public sealed class MenuItem
{
    public required int MenuItemId { get; init; }
    public required string Name { get; init; }

    // Enforced at order time via PlaceOrderCommand.
    public required MenuItemCategory Category { get; init; }
    public required ItemPhase Course { get; init; }

    // Null = not cooked at a station — picked up from a passive source instead.
    public string? RequiredStationType { get; init; }

    // Set only when RequiredStationType is null (e.g. "freezer", "bar", "pantry").
    public string? PickupSourceType { get; init; }

    // Authored in real-world seconds; converted to PrepTicks at load time (mechanics doc §14).
    public required float PrepTimeSeconds { get; init; }
    public required decimal DefaultPrice { get; init; }

    // References MealPeriodDefinition.MealPeriodId.
    public required IReadOnlyList<string> AvailableMealPeriods { get; init; }
}
