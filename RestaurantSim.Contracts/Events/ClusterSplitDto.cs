namespace RestaurantSim.Contracts.Events;

public sealed record ClusterRestoredDto(int ClusterId, IReadOnlyList<int> TableIds, int Capacity);

// MergedClusterId stops being a merged cluster; Restored are the original clusters put back
// (one of them reuses MergedClusterId). Free by construction: a split only happens on a free cluster.
public sealed record ClusterSplitDto(int MergedClusterId, IReadOnlyList<ClusterRestoredDto> Restored, long Tick);
