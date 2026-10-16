namespace RestaurantSim.Contracts.Events;

// Wire contract for a seated group. Tick is the SimulationTick the event
// happened at — the client drops events with Tick < its snapshot's tick and
// applies equal-tick events idempotently (set state, never toggle).
public sealed record GroupSeatedDto(int GroupId, int ClusterId, long Tick);
