namespace RestaurantSim.Domain.Floor;

// One table sliding along its lane (staff pushing it against its neighbour, or back home).
public sealed record TableMove(int TableId, GridPosition From, GridPosition To)
{
    // Tables only ever slide along a straight lane, so this is a plain cell count.
    public int DistanceCells => Math.Abs(To.X - From.X) + Math.Abs(To.Y - From.Y);
}

// How a set of clusters merges: which cluster's tables stay put, which waiter owns the
// result (null when none of the merged clusters has one — the caller falls back), and the
// slides that bring every other table against the stayer.
public sealed record MergeMovePlan(int StayingClusterId, int? WaiterId, IReadOnlyList<TableMove> Moves);
