namespace RestaurantSim.Domain.Scheduling;

// Extracted from SpawnArrivalCommandHandler's private implementation — Ordering
// needs the same "which meal period is active right now" check (for
// MenuItem.AvailableMealPeriods gating), so this is now a shared utility instead
// of a second copy-paste.
public static class MealPeriodCalculator
{
    public static MealPeriodDefinition? FindActive(
        IReadOnlyList<MealPeriodDefinition> activePeriods, int minutesPerTick, long currentTick)
    {
        var minutesIntoDay = MinutesIntoDay(minutesPerTick, currentTick);

        foreach (var period in activePeriods)
        {
            var (startMinutes, endMinutes) = PeriodWindow(period);
            if (minutesIntoDay >= startMinutes && minutesIntoDay < endMinutes)
                return period;
        }

        return null;
    }

    public static double MinutesIntoDay(int minutesPerTick, long currentTick)
    {
        var ticksPerDay = (long)Math.Round(1440.0 / minutesPerTick);
        var timeOfDayTicks = currentTick % ticksPerDay;
        return timeOfDayTicks * minutesPerTick;
    }

    public static (int StartMinutes, int EndMinutes) PeriodWindow(MealPeriodDefinition period)
    {
        var startMinutes = period.StartTime.Hour * 60 + period.StartTime.Minute;

        // A period ending at 00:00 means "end of day" (e.g. dinner 19:00-00:00),
        // not literal midnight-as-zero. Genuine post-midnight wraparound (e.g. a
        // future 22:00-02:00 period) isn't handled by this first-pass logic.
        var endMinutes = period.EndTime is { Hour: 0, Minute: 0 }
            ? 1440
            : period.EndTime.Hour * 60 + period.EndTime.Minute;

        return (startMinutes, endMinutes);
    }
}
