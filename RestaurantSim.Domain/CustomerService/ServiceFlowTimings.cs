using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Domain.CustomerService;

public static class ServiceFlowTimings
{
    // attempt 1 = the first visit. Each recheck halves the not-ready odds:
    // P(ready, attempt n) = 1 - (1 - base) / 2^(n-1). An attempt below 1 counts as 1.
    public static float ReadyToOrderChance(float baseChance, int attempt)
    {
        var rechecks = Math.Max(1, attempt) - 1;
        var notReady = (1f - baseChance) / MathF.Pow(2f, rechecks);
        return 1f - notReady;
    }

    // Eating time for one item: uniform in [Min, Max] seconds, converted to ticks.
    public static long RollEatingTicks(DurationRange range, int ticksPerRealSecond, IRandomProvider random)
    {
        var seconds = range.MinSeconds + (float)random.NextDouble() * (range.MaxSeconds - range.MinSeconds);
        return TickRates.SecondsToTicks(seconds, ticksPerRealSecond);
    }
}
