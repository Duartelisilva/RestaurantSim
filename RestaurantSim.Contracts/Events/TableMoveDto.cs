using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Contracts.Events;

// One table sliding along its lane. `To` is where the table now logically is (the snapshot
// shows it there too); a client animates it from `From` to `To` over the event's DurationTicks.
public sealed record TableMoveDto(int TableId, GridPointDto From, GridPointDto To);
