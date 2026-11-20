using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.State;

// A table that is currently sliding (merge or return): the floor view animates it from
// `From` to `To` over `DurationSeconds` of real time. Client-only state, never sent by the
// server; the snapshot already shows the table at `To`.
public sealed record TableMotion(GridPointDto From, GridPointDto To, double DurationSeconds);
