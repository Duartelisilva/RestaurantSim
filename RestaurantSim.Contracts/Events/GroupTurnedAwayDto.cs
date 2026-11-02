namespace RestaurantSim.Contracts.Events;

// Wire names of the Domain TurnedAwayReason enum; Infrastructure.Tests pins them to its members.
public static class TurnedAwayReasonNames
{
    public const string QueueFull = "QueueFull";
    public const string PartyTooLarge = "PartyTooLarge";
}

// Reputation is the absolute value after the penalty (see GroupAbandonedQueueDto).
public sealed record GroupTurnedAwayDto(float Reputation, long Tick, string Reason);
