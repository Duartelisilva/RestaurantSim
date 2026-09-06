using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.AdvanceStaffShift;

// Sim-loop-dispatched. Handles both directions of shift transition (clock-in
// at ShiftStart, coverage-gated clock-out at ShiftEnd — entity doc §24a) and
// Fatigue accrual (mechanics doc §13a) in one command, since Fatigue's reset
// specifically happens ON the transition into OffShift — the two are
// intentionally bundled, not split. Not yet wired into RestaurantTickProcessor.
public sealed record AdvanceStaffShiftCommand(Guid RestaurantId) : IRequest<Result>, IRestaurantScopedRequest;
