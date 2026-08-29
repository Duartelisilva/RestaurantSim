using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.SpawnArrival;

// Sim-loop-dispatched (like EndDayCommand, mechanics doc §15), not player-facing.
// Not yet wired to an actual tick loop — that's Phase 5. For now this can be
// invoked directly/manually or from a test.
public sealed record SpawnArrivalCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
