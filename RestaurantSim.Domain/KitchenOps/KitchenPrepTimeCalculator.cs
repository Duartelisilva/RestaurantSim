using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Domain.KitchenOps;

// Shared prep-time math (mechanics doc §10) — single source of truth used by
// both PlaceOrderCommandHandler (nominal, pre-cook-assignment estimate for
// course-sync targets) and AdvanceKitchenCommandHandler (actual, once a real
// cook is assigned). Consistent with the project's no-duplicated-logic
// convention (CLAUDE.md, added this session).
public static class KitchenPrepTimeCalculator
{
    public static long PrepTicks(float prepTimeSeconds, int ticksPerRealSecond) =>
        Math.Max(1, (long)Math.Round(prepTimeSeconds * ticksPerRealSecond));

    // Nominal — baseline Speed=0.5/Fatigue=0 (entity doc §7's stated "average,
    // no modifier" default) — both multipliers resolve to exactly 1.0 at this
    // baseline, used before a real cook is assigned (ticket-submission time).
    public static long NominalEffectivePrepTicks(MenuItem menuItem, float stationSpeedMultiplier, int ticksPerRealSecond)
    {
        var prepTicks = PrepTicks(menuItem.PrepTimeSeconds, ticksPerRealSecond);
        return (long)Math.Round(prepTicks * stationSpeedMultiplier);
    }

    // Actual — using the real assigned cook's stats, once known.
    public static long EffectivePrepTicks(MenuItem menuItem, float stationSpeedMultiplier, StaffStats cookStats, int ticksPerRealSecond)
    {
        var prepTicks = PrepTicks(menuItem.PrepTimeSeconds, ticksPerRealSecond);
        var cookSpeedMultiplier = 1.6 - 1.2 * cookStats.Get(StatType.Speed);
        var fatigueMultiplier = 1.0 + 0.5 * cookStats.Get(StatType.Fatigue);
        return (long)Math.Round(prepTicks * stationSpeedMultiplier * cookSpeedMultiplier * fatigueMultiplier);
    }
}
