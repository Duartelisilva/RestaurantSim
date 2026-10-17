namespace RestaurantSim.Contracts.Events;

// Reputation is the absolute value after the penalty (see GroupAbandonedQueueDto).
public sealed record GroupTurnedAwayDto(float Reputation, long Tick);
