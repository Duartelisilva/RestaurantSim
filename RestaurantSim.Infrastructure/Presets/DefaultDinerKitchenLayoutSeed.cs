using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Infrastructure.Presets;

// presets.md's Table Layout section only ever gave COUNTS for kitchen stations
// ("1x grill, 1x fryer, 1x cold_prep, 1x dessert_station") — Capacity and
// SpeedMultiplier per type were never actually specified anywhere, unlike most
// other "open, pending playtesting" values which are at least acknowledged as
// open. These are first-pass proposed defaults, flagged the same way.
//
// HeatLamp.SlotCount = 3 IS explicitly given in presets.md, used as-is.
//
// Not wired into a real bootstrap yet — no CreateRestaurantCommand exists to
// attach this to a fresh Restaurant.Floor. Exists now so PlaceOrderCommand's
// course-sync lookup and AdvanceKitchenCommand's contention logic have real
// data to consume; a future bootstrap command calls this the same way it will
// call DefaultDinerConfigSeed/DefaultDinerMenuSeed.
public static class DefaultDinerKitchenLayoutSeed
{
    public static IReadOnlyList<KitchenStationType> CreateStationTypes() => new List<KitchenStationType>
    {
        new() { StationTypeId = "grill", Capacity = 2, SpeedMultiplier = 1.0f },
        new() { StationTypeId = "fryer", Capacity = 2, SpeedMultiplier = 1.0f },
        new() { StationTypeId = "cold_prep", Capacity = 1, SpeedMultiplier = 1.0f },
        new() { StationTypeId = "dessert_station", Capacity = 2, SpeedMultiplier = 1.0f }
    };

    public static IReadOnlyList<KitchenStation> CreateStations(IReadOnlyList<KitchenStationType> stationTypes)
    {
        var byId = stationTypes.ToDictionary(t => t.StationTypeId);
        return new List<KitchenStation>
        {
            new() { StationId = 1, Position = new GridPosition(0, 4), Hitbox = new GridBounds(1, 1), StationType = byId["grill"] },
            new() { StationId = 2, Position = new GridPosition(1, 4), Hitbox = new GridBounds(1, 1), StationType = byId["fryer"] },
            new() { StationId = 3, Position = new GridPosition(2, 4), Hitbox = new GridBounds(1, 1), StationType = byId["cold_prep"] },
            new() { StationId = 4, Position = new GridPosition(3, 4), Hitbox = new GridBounds(1, 1), StationType = byId["dessert_station"] }
        };
    }

    public static HeatLamp CreateHeatLamp() => new()
    {
        HeatLampId = 1,
        Position = new GridPosition(1, 4),
        Hitbox = new GridBounds(1, 1),
        SlotCount = 3, // presets.md — explicitly given
        QualityDecayRatePerTick = 1.0f // NOT given in presets.md either — first-pass proposed default
    };
}
