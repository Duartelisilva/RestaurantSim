using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.LeaveTable;

// Represents the physical departure of an ALREADY-PAID group (requires
// LifecycleState == Leaving, set by PayBillCommand). A mid-meal walkout
// without paying is a different, unbuilt mechanic — not queue-abandonment-
// adjacent, no mood-driven trigger exists for a seated group yet.
public sealed record LeaveTableCommand(Guid RestaurantId, int GroupId) : IRequest<Result>, IRestaurantScopedRequest;
