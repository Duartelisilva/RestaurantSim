using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.CleanTable;

// No equivalent name was ever locked for tables in CLAUDE.md (only
// "BussersCleanBathroomCommand" is named) — real, if minor, doc gap. Named
// to match the project's VerbNounCommand convention.
public sealed record CleanTableCommand(Guid RestaurantId, int BusserStaffId, int TableId) : IRequest<Result>, IRestaurantScopedRequest;
