using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.EndDay;

// Sim-loop-dispatched (mechanics doc §15), not player-facing. Checks
// DayEndEligible internally and no-ops if not yet eligible — same
// self-checking pattern as SpawnArrivalCommand, rather than requiring the
// caller to pre-check. NOT yet wired into RestaurantTickProcessor's step 5
// (day/meal-period boundary check, architecture doc §2a) — add there once
// this is confirmed working.
public sealed record EndDayCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
