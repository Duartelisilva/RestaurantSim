using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Business logic §1.2: queue at max length, group turned away, reputation hit.
public sealed record GroupTurnedAway(Guid RestaurantId, long Tick) : INotification;
