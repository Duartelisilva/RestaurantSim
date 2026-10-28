using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record ClusterRestored(int ClusterId, IReadOnlyList<int> TableIds, int Capacity);

// MergedClusterId no longer exists as a merged cluster; Restored are the original
// clusters put back (one of them reuses the merged cluster's id).
public sealed record ClusterSplit(
    Guid RestaurantId,
    int MergedClusterId,
    IReadOnlyList<ClusterRestored> Restored,
    long Tick) : INotification;
