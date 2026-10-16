using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Application.Queries.GetRestaurantSnapshot;

// Implements IRestaurantScopedRequest so the snapshot is read inside the
// per-restaurant lock — no torn read against the tick loop (architecture doc §11).
public sealed record GetRestaurantSnapshotQuery(Guid RestaurantId)
    : IRequest<Result<RestaurantSnapshotDto>>, IRestaurantScopedRequest;
