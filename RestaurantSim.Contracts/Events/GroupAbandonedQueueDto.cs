namespace RestaurantSim.Contracts.Events;

// Reputation is the absolute value after the penalty, not a delta — the client
// sets state from it, so a duplicated or replayed event cannot double-apply.
public sealed record GroupAbandonedQueueDto(int GroupId, float Reputation, long Tick);
