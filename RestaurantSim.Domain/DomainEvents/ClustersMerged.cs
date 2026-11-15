using MediatR;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Domain.DomainEvents;

// ClusterId is the surviving cluster (the lowest merged id); the other merged ids
// no longer exist afterwards. Moves are the tables that slide together (empty when they
// already touched); DurationTicks is how long the slide takes, 0 when nothing moves. The
// cluster is Moving until then — TableMoveCompleted says when it is usable.
public sealed record ClustersMerged(
    Guid RestaurantId,
    int ClusterId,
    IReadOnlyList<int> MergedClusterIds,
    IReadOnlyList<int> TableIds,
    int Capacity,
    IReadOnlyList<TableMove> Moves,
    long DurationTicks,
    long Tick) : INotification;
