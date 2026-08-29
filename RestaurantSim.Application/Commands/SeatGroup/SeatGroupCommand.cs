using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.SeatGroup;

// Takes an already-decided ClusterId — the "which table" decision (greedy
// best-fit per business logic §1.3, or ISeatingStrategy for AutoManager) happens
// upstream. The command itself only commits a specific, already-chosen seating
// action, so both a human player and AutoManager can call the exact same surface.
public sealed record SeatGroupCommand(Guid RestaurantId, int GroupId, int ClusterId)
    : IRequest<Result>, IRestaurantScopedRequest;
