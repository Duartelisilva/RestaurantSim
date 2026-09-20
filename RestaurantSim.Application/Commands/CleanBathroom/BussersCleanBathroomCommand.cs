using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.CleanBathroom;

// Name matches CLAUDE.md's exact locked naming ("BussersCleanBathroomCommand
// — Busser cleaning task dispatch, mechanics doc §13") even though it departs
// slightly from the pure VerbNounCommand convention.
public sealed record BussersCleanBathroomCommand(Guid RestaurantId, int BusserStaffId, int BathroomId) : IRequest<Result>, IRestaurantScopedRequest;
