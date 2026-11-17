namespace RestaurantSim.Contracts.Events;

public sealed record ClusterRestoredDto(int ClusterId, IReadOnlyList<int> TableIds, int Capacity);

// MergedClusterId stops being a merged cluster; Restored are the original clusters put back
// (one of them reuses MergedClusterId). Moves slide the tables back to their home positions
// (empty if they never left); the restored clusters are Moving for DurationTicks, until a
// TableMoveCompletedDto says otherwise.
public sealed record ClusterSplitDto(
    int MergedClusterId,
    IReadOnlyList<ClusterRestoredDto> Restored,
    IReadOnlyList<TableMoveDto> Moves,
    long DurationTicks,
    long Tick);
