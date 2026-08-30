using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Business logic §1.2: group's average Mood dropped below threshold while
// queued, they leave.
// on other customers, no complaints-book interaction. Confirmed design
public sealed record GroupAbandonedQueue(Guid RestaurantId, int GroupId, long Tick) : INotification;
