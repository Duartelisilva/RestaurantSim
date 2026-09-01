using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.AdvanceKitchen;

// Sim-loop-dispatched (like SpawnArrivalCommand/ProcessQueueAbandonmentCommand),
// not player-facing. Not yet wired to an actual tick loop (Phase 5) — invoked
// directly/manually or from a test for now. Handles station-cooked items only
// (EntryMain/Dessert) — pickup items (Drinks) have no station/contention model
// and are a separate, not-yet-built waiter-fetch mechanic.
public sealed record AdvanceKitchenCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
