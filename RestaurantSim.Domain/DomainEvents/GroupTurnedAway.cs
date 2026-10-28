using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

public enum TurnedAwayReason
{
    QueueFull,
    PartyTooLarge
}

// Reason defaults to QueueFull so existing publishers/constructors keep compiling.
public sealed record GroupTurnedAway(
    Guid RestaurantId,
    float ReputationAfter,
    long Tick,
    TurnedAwayReason Reason = TurnedAwayReason.QueueFull) : INotification;
