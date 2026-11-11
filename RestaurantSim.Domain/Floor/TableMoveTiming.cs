using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Domain.Floor;

// Table moves are timed (mechanics doc §18, draft): the longest slide of one merge/split
// sets how long the whole thing takes, authored in real seconds per cell and converted
// once per mechanics doc §14.
public static class TableMoveTiming
{
    // "Fairly short": first-pass default, pending playtesting.
    public const float DefaultSecondsPerCell = 2f;

    public static int MaxMergeGapCells(RestaurantConfig config) => Math.Max(0, config.MaxMergeGapCells ?? 0);

    public static float SecondsPerCell(RestaurantConfig config) => config.TableMoveSecondsPerCell ?? DefaultSecondsPerCell;

    // 0 when nothing moves (tables already touching): no Moving phase at all.
    public static long DurationTicks(IReadOnlyCollection<TableMove> moves, RestaurantConfig config)
    {
        if (moves.Count == 0)
            return 0;

        var longestSlide = moves.Max(m => m.DistanceCells);
        return TickRates.SecondsToTicks(longestSlide * SecondsPerCell(config), config.TicksPerRealSecond);
    }
}
