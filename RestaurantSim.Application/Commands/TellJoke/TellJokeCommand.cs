using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.TellJoke;

// Shared by both a human player's explicit choice and AutoManager's own
// trigger decision (IWaiterActionStrategy) — same command surface, per
// architecture doc §5's principle that AutoManager never special-cases
// anything a manual player couldn't also do. TargetCustomerId null means
// table-targeted (mechanics doc §11); set means person-targeted.
public sealed record TellJokeCommand(Guid RestaurantId, int ClusterId, int? TargetCustomerId) : IRequest<Result>, IRestaurantScopedRequest;
