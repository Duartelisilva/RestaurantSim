using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Past-tense fact: the tables of this cluster have finished sliding (into a merge or back
// home), so the cluster stops being Moving and can be seated / merged again.
public sealed record TableMoveCompleted(Guid RestaurantId, int ClusterId, long Tick) : INotification;
