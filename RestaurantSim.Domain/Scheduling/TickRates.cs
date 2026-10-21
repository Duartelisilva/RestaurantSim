namespace RestaurantSim.Domain.Scheduling;

// Durations are authored in real-world seconds and rates per real-world second
// (mechanics doc §14); these helpers convert them to what a single tick applies.
// Changing TicksPerRealSecond therefore never changes real-time behaviour.
public static class TickRates
{
    // Rate authored per real second -> amount applied each tick.
    public static float PerTick(float ratePerSecond, int ticksPerRealSecond)
    {
        return ratePerSecond / Math.Max(1, ticksPerRealSecond);
    }

    // mechanics doc §14: max(1, round(seconds x TicksPerRealSecond)), round-half-up.
    // No duration ever resolves to 0 ticks (an instant, free action).
    public static long SecondsToTicks(float seconds, int ticksPerRealSecond)
    {
        var ticks = Math.Round(seconds * Math.Max(1, ticksPerRealSecond), MidpointRounding.AwayFromZero);
        return Math.Max(1L, (long)ticks);
    }
}
