using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.AdvanceDining;

// Tick-dispatched (architecture doc §2a, step 1, right after AdvanceKitchenCommand —
// it needs the items the kitchen marked Ready this same tick). Drives every seated
// group through ordering, eating, paying and leaving (mechanics doc §17, draft).
// Not player-facing and not an AutoManager decision: customers do this on their own.
public sealed record AdvanceDiningCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
