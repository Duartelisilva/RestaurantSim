namespace RestaurantSim.Domain.Scheduling;

// Rates are authored per real-world second (like durations, mechanics doc §14)
// and applied per tick as rate / TicksPerRealSecond, so changing the tick rate
// never changes how fast Mood, Fatigue or food quality move in real time.
public static class TickRates
{
    public static float PerTick(float ratePerSecond, int ticksPerRealSecond)
    {
        return ratePerSecond / Math.Max(1, ticksPerRealSecond);
    }
}
