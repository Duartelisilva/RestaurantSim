namespace RestaurantSim.Domain.Statistics;

// Running totals for the CURRENT day only — reset by EndDayCommand right
// after appending a snapshot. Lives on Restaurant itself (not a separate
// store) so it persists across ticks via the normal repository round-trip,
// same as everything else on the aggregate.
public sealed class DailyStatsAccumulator
{
    public decimal Revenue { get; set; }

    // Counts individual people (PartySize-weighted), not visits/groups.
    public int CustomersServed { get; set; }

    // Weighted sum of (group's FinalAverageMood x PartySize) — divided by
    // CustomersServed at snapshot time for a genuinely people-weighted
    // average, not an unweighted average of group-averages.
    public float WeightedFinalMoodSum { get; set; }

    public float ReputationAtDayStart { get; set; }

    // Per-tick occupancy sampling (TableUtilization) — not event-driven,
    // sampled directly in RestaurantTickProcessor each tick.
    public float TableOccupancySampleSum { get; set; }
    public long TableOccupancySampleCount { get; set; }

    public void Reset(float reputationAtNewDayStart)
    {
        Revenue = 0;
        CustomersServed = 0;
        WeightedFinalMoodSum = 0;
        TableOccupancySampleSum = 0;
        TableOccupancySampleCount = 0;
        ReputationAtDayStart = reputationAtNewDayStart;
    }
}
