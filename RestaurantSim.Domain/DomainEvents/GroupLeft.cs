using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Name matches entity doc §21's own reference to this event
// (CustomerGroupBoundResolution "subscribes to GroupLeft").
public sealed record GroupLeft(Guid RestaurantId, int GroupId, int ClusterId, float FinalAverageMood, long Tick) : INotification;
