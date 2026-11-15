using MediatR;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record ClusterRestored(int ClusterId, IReadOnlyList<int> TableIds, int Capacity);

// MergedClusterId no longer exists as a merged cluster; Restored are the original
// clusters put back (one of them reuses the merged cluster's id). Moves slide the tables
// back to their home positions (empty if they never left); the restored clusters are Moving
// for DurationTicks.
public sealed record ClusterSplit(
    Guid RestaurantId,
    int MergedClusterId,
    IReadOnlyList<ClusterRestored> Restored,
    IReadOnlyList<TableMove> Moves,
    long DurationTicks,
    long Tick) : INotification;
