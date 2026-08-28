namespace RestaurantSim.Domain.Statistics;

// Value type — stored in a rolling log via ISnapshotStore, never held on the
// hot Restaurant aggregate itself (architecture doc §7.5).
public readonly record struct StatisticsSnapshot(
    long Tick,
    decimal Revenue,
    int CustomersServed,
    float AvgWaitTicks,
    float TableUtilization,
    float AvgFinalMood,
    int ComplaintCount,
    IReadOnlyList<string> ActiveEventTypes,
    int TotalGameplayDays,
    decimal Costs,
    float ReputationDelta)
{
    // Derived, not stored independently.
    public decimal NetProfit => Revenue - Costs;
}
