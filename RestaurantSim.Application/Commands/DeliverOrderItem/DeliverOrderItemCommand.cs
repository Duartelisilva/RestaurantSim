using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.DeliverOrderItem;

// First-pass, flat delivery — no real distance-based travel time (needs the
// Phase 5+ pathfinder, mechanics doc §9). Gated only on an idle waiter
// existing; doesn't reserve/occupy the waiter's time since no duration is
// modeled yet. Real travel-time realism (a table far from the kitchen
// actually waiting longer) is deferred, not implemented here.
public sealed record DeliverOrderItemCommand(Guid RestaurantId, int OrderItemId) : IRequest<Result>, IRestaurantScopedRequest;
