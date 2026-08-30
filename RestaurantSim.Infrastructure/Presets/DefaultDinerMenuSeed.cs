using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Infrastructure.Presets;

// Temporary C# stand-in for real JSON menu presets (architecture doc §9) —
// transcribes restaurant-simulator-presets.md's "default_diner" menu catalog
// (16 items) directly, same pattern as DefaultDinerConfigSeed.
public static class DefaultDinerMenuSeed
{
    private static readonly string[] Periods = { "lunch", "dinner" };

    public static IReadOnlyList<MenuItem> Create() => new List<MenuItem>
    {
        // Beverages (Drinks phase) — pickup from Freezer, not station-cooked
        new() { MenuItemId = 1, Name = "Water", Category = MenuItemCategory.Adult, Course = ItemPhase.Drinks, RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 1.50m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 2, Name = "Soda", Category = MenuItemCategory.Adult, Course = ItemPhase.Drinks, RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 2.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 3, Name = "House Wine (glass)", Category = MenuItemCategory.Adult, Course = ItemPhase.Drinks, RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 3.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 4, Name = "Orange Juice", Category = MenuItemCategory.Kids, Course = ItemPhase.Drinks, RequiredStationType = null, PickupSourceType = "freezer", PrepTimeSeconds = 2, DefaultPrice = 2.00m, AvailableMealPeriods = Periods },

        // Mains (EntryMain phase)
        new() { MenuItemId = 5, Name = "Grilled Steak", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain, RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 14, DefaultPrice = 12.50m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 6, Name = "Grilled Chicken", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain, RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 12, DefaultPrice = 10.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 7, Name = "Grilled Fish", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain, RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 13, DefaultPrice = 13.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 8, Name = "Steak & Egg with Fries", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain, RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 10, DefaultPrice = 9.50m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 9, Name = "Stuffed Toasted Sandwich", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain, RequiredStationType = "fryer", PickupSourceType = null, PrepTimeSeconds = 11, DefaultPrice = 11.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 10, Name = "Shredded Cod & Fried Potato", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain, RequiredStationType = "fryer", PickupSourceType = null, PrepTimeSeconds = 9, DefaultPrice = 10.50m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 11, Name = "Caesar Salad", Category = MenuItemCategory.Adult, Course = ItemPhase.EntryMain, RequiredStationType = "cold_prep", PickupSourceType = null, PrepTimeSeconds = 6, DefaultPrice = 8.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 12, Name = "Chicken Nuggets", Category = MenuItemCategory.Kids, Course = ItemPhase.EntryMain, RequiredStationType = "fryer", PickupSourceType = null, PrepTimeSeconds = 7, DefaultPrice = 6.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 13, Name = "Pasta with Sauce", Category = MenuItemCategory.Kids, Course = ItemPhase.EntryMain, RequiredStationType = "grill", PickupSourceType = null, PrepTimeSeconds = 8, DefaultPrice = 5.50m, AvailableMealPeriods = Periods },

        // Desserts (Dessert phase)
        new() { MenuItemId = 14, Name = "Custard Tart", Category = MenuItemCategory.Adult, Course = ItemPhase.Dessert, RequiredStationType = "dessert_station", PickupSourceType = null, PrepTimeSeconds = 3, DefaultPrice = 2.00m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 15, Name = "Chocolate Mousse", Category = MenuItemCategory.Adult, Course = ItemPhase.Dessert, RequiredStationType = "dessert_station", PickupSourceType = null, PrepTimeSeconds = 4, DefaultPrice = 3.50m, AvailableMealPeriods = Periods },
        new() { MenuItemId = 16, Name = "Ice Cream", Category = MenuItemCategory.Kids, Course = ItemPhase.Dessert, RequiredStationType = "dessert_station", PickupSourceType = null, PrepTimeSeconds = 3, DefaultPrice = 2.50m, AvailableMealPeriods = Periods }
    };
}
