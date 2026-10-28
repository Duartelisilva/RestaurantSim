using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.MergeClusters;

// Pushes free, touching clusters together into one bigger one. Used by the AutoManager's
// seating strategy; the same command is what a manual player will use later.
public sealed record MergeClustersCommand(Guid RestaurantId, IReadOnlyList<int> ClusterIds)
    : IRequest<Result>, IRestaurantScopedRequest;
