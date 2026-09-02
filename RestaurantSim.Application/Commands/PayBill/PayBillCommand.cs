using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.PayBill;

// Skips modeling the realtime "waiter walks over with the check" delay
// (CustomerCall/RequestBill lifecycle isn't built — same "represent the
// completed action, not the dispatch delay" pattern used throughout Phase 4).
// Represents the bill actually being paid: tip calculation, revenue, and the
// CoursePhase/LifecycleState transition toward Leaving.
public sealed record PayBillCommand(Guid RestaurantId, int GroupId) : IRequest<Result>, IRestaurantScopedRequest;
