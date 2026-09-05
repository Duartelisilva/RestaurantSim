using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.CreateRestaurant;

// Deliberately NOT IRestaurantScopedRequest — there's no existing RestaurantId
// to lock on, since this command is what creates one. RestaurantLockBehavior
// already handles this gracefully (skips locking for non-scoped requests).
public sealed record CreateRestaurantCommand(string ConfigId) : IRequest<Result<Guid>>;
