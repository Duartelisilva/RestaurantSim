namespace RestaurantSim.Contracts.Events;

// The tables of this cluster have stopped sliding: it is no longer Moving and can be
// seated or merged again. Absolute, like every state event — a client sets the cluster's
// state, a duplicate changes nothing.
public sealed record TableMoveCompletedDto(int ClusterId, long Tick);
