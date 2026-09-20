using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.AdvanceCleaningProgress;

// Sim-loop-dispatched — progresses every Busser currently mid-task
// (mechanics doc §13), same tick-driven pattern as AdvanceKitchenCommand.
// Not yet wired into RestaurantTickProcessor.
public sealed record AdvanceCleaningProgressCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
