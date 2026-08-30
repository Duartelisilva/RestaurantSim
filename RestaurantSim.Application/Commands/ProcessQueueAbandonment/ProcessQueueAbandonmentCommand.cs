using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.ProcessQueueAbandonment;

// Sim-loop-dispatched (like SpawnArrivalCommand/EndDayCommand), not player-facing.
// Deterministic — evaluated every tick, not throttled to ProbabilityCheckpoint-
// IntervalSeconds (per the now-locked intra-tick execution order, step 4).
// Not yet wired to an actual tick loop (Phase 5) — invoked directly/manually or
// from a test for now.
public sealed record ProcessQueueAbandonmentCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
