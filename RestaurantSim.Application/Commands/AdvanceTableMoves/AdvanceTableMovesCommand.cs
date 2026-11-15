using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.AdvanceTableMoves;

// Sim-loop-dispatched, step 1 of the tick (architecture doc §2a: resolve scheduled
// completions): tables whose slide has reached its end tick stop being "moving".
public sealed record AdvanceTableMovesCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
