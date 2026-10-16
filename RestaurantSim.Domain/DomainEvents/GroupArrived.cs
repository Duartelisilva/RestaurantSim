using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// PartySize added so clients can show the queued group without a snapshot
// refetch (same reasoning as PartySize on GroupLeft).
public sealed record GroupArrived(Guid RestaurantId, int GroupId, int PartySize, long Tick) : INotification;
