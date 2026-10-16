namespace RestaurantSim.Contracts.Events;

public sealed record GroupAbandonedQueueDto(int GroupId, long Tick);
