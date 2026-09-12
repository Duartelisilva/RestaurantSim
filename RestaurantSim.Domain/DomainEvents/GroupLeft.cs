using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// PartySize added — without it, a stats subscriber could only count VISITS,
// not actual people, making "CustomersServed" (entity doc §23) mean the
// wrong thing.
public sealed record GroupLeft(Guid RestaurantId, int GroupId, int ClusterId, int PartySize, float FinalAverageMood, long Tick) : INotification;
