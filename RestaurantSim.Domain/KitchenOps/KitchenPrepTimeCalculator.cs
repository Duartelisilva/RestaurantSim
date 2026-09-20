using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Domain.KitchenOps;

public static class KitchenPrepTimeCalculator
{
    public static long PrepTicks(float prepTimeSeconds, int ticksPerRealSecond) =>
        Math.Max(1, (long)Math.Round(prepTimeSeconds * ticksPerRealSecond));

    public static long NominalEffectivePrepTicks(MenuItem menuItem, float stationSpeedMultiplier, int ticksPerRealSecond)
    {
        var prepTicks = PrepTicks(menuItem.PrepTimeSeconds, ticksPerRealSecond);
        return (long)Math.Round(prepTicks * stationSpeedMultiplier);
    }

    public static long EffectivePrepTicks(MenuItem menuItem, float stationSpeedMultiplier, StaffStats cookStats, int ticksPerRealSecond)
    {
        var prepTicks = PrepTicks(menuItem.PrepTimeSeconds, ticksPerRealSecond);
        var cookSpeedMultiplier = PerformanceMultipliers.SpeedMultiplier(cookStats.Get(StatType.Speed));
        var fatigueMultiplier = PerformanceMultipliers.FatigueMultiplier(cookStats.Get(StatType.Fatigue));
        return (long)Math.Round(prepTicks * stationSpeedMultiplier * cookSpeedMultiplier * fatigueMultiplier);
    }
}
