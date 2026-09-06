using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.AdvanceMood;

// Sim-loop-dispatched, implements the locked intra-tick order's step 2
// ("continuous per-tick accumulation" — architecture doc §2a). Not yet wired
// to an actual tick dispatch call (RestaurantTickProcessor needs updating to
// call this too) — invoked directly/manually or from a test for now.
public sealed record AdvanceMoodCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
