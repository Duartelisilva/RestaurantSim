namespace RestaurantSim.Domain.Scheduling;

// Extracted from SpawnArrivalCommandHandler's private implementation — Ordering
// needs the same "which meal period is active right now" check (for
// MenuItem.AvailableMealPeriods gating), so this is now a shared utility instead
// of a second copy-paste.
public static class MealPeriodCalculator
{
    public static MealPeriodDefinition? FindActive(
        IReadOnlyList<MealPeriodDefinition> activePeriods, double minutesPerTick, long currentTick)
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

    // The period a group is served under: the active one, otherwise the one that ended
    // most recently. A group seated before closing is served out normally (business
    // logic §1.1, mechanics doc §15), so after the period ends it still orders under the
    // period it sat down in rather than being unable to order at all. Past midnight (no
    // period has "ended" yet today) it falls back to the day's last-ending period.
    public static MealPeriodDefinition? FindServicePeriod(
        IReadOnlyList<MealPeriodDefinition> activePeriods, double minutesPerTick, long currentTick)
    {
        var active = FindActive(activePeriods, minutesPerTick, currentTick);
        if (active is not null || activePeriods.Count == 0)
            return active;

        var minutesIntoDay = MinutesIntoDay(minutesPerTick, currentTick);

        var mostRecentlyEnded = activePeriods
            .Where(p => PeriodWindow(p).EndMinutes <= minutesIntoDay)
            .OrderByDescending(p => PeriodWindow(p).EndMinutes)
            .FirstOrDefault();

        return mostRecentlyEnded ?? activePeriods.OrderByDescending(p => PeriodWindow(p).EndMinutes).First();
    }

    public static double MinutesIntoDay(double minutesPerTick, long currentTick)
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

    // Earliest StartTime among the active periods, in minutes into the day
    // (mechanics doc §15: Opening). Requires at least one period.
    public static int OpeningMinutes(IReadOnlyList<MealPeriodDefinition> activePeriods)
    {
        return activePeriods.Select(PeriodWindow).Min(w => w.StartMinutes);
    }

    // The tick a brand-new restaurant starts at: leadMinutes of in-game time before
    // Opening, clamped to the start of day (so the lead is "at most" leadMinutes).
    // A negative lead is treated as 0; no active periods means tick 0.
    public static long InitialTick(
        IReadOnlyList<MealPeriodDefinition> activePeriods, double minutesPerTick, int leadMinutes)
    {
        if (activePeriods.Count == 0)
            return 0;

        var startMinutes = Math.Max(0, OpeningMinutes(activePeriods) - Math.Max(0, leadMinutes));
        return (long)Math.Round(startMinutes / minutesPerTick);
    }
}
