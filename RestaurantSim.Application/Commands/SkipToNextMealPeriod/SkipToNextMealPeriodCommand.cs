using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.SkipToNextMealPeriod;

// Player-facing, optional convenience (mechanics doc §15) — for an INTRA-day
// gap between two of the restaurant's own active periods (e.g. 15:00-19:00
// between lunch and dinner). Distinct from EndDayCommand, which handles the
// true end-of-day boundary and auto-advances without player action.
public sealed record SkipToNextMealPeriodCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
