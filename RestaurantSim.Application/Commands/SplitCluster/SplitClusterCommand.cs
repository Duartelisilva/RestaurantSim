using MediatR;
using RestaurantSim.Application.Common;

namespace RestaurantSim.Application.Commands.SplitCluster;

// Puts a runtime-merged cluster back as the clusters it was made from ("return to
// their normal state"). Only clusters created by MergeClustersCommand can be split;
// ones authored pre-merged in the layout are that table group's normal state.
public sealed record SplitClusterCommand(Guid RestaurantId, int ClusterId)
    : IRequest<Result>, IRestaurantScopedRequest;
