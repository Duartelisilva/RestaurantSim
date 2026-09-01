using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Fires when a ticket flips to async mode (mechanics doc §10's re-inform path).
// Consumed by an Infrastructure handler to apply RestaurantConfig.ReinformMoodPenalty
// to the group's still-waiting members — new mechanic, confirmed in chat, not yet
// backported into the locked docs.
public sealed record TicketReinformed(Guid RestaurantId, int TicketId, int GroupId, long Tick) : INotification;
