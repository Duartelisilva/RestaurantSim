using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.SpreadGroupMood;

// Sim-loop-dispatched, implements the locked intra-tick order's step 3
// (architecture doc §2a) — must run AFTER AdvanceMoodCommand (step 2) so it
// reads this tick's already-decayed Mood, not a stale pre-event snapshot.
// Not yet wired into RestaurantTickProcessor.
public sealed record SpreadGroupMoodCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
