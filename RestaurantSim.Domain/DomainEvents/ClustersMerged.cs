using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// ClusterId is the surviving cluster (the lowest merged id); the other merged ids
// no longer exist afterwards.
public sealed record ClustersMerged(
    Guid RestaurantId,
    int ClusterId,
    IReadOnlyList<int> MergedClusterIds,
    IReadOnlyList<int> TableIds,
    int Capacity,
    long Tick) : INotification;
