using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Application.Commands.SplitCluster;

public sealed class SplitClusterCommandHandler : IRequestHandler<SplitClusterCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPublisher _publisher;

    public SplitClusterCommandHandler(IRestaurantRepository repository, IPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(SplitClusterCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var floor = restaurant.Floor;
        var cluster = floor.Clusters.FirstOrDefault(c => c.ClusterId == request.ClusterId);
        if (cluster is null)
            return Result.Failure($"Cluster {request.ClusterId} does not exist.");

        if (cluster.MergedFrom is not { Count: > 0 } composition)
            return Result.Failure($"Cluster {request.ClusterId} was not created by a merge and cannot be split.");

        if (!FloorQueries.FreeClusterIds(restaurant).Contains(cluster.ClusterId))
            return Result.Failure($"Cluster {request.ClusterId} must be free and clean to be split.");

        // ---- everything checked; commit ----
        var restored = new List<ClusterRestored>();

        foreach (var original in composition)
        {
            var target = original.ClusterId == cluster.ClusterId
                ? cluster
                : new TableCluster { ClusterId = original.ClusterId, TableIds = original.TableIds.ToList(), Capacity = original.Capacity };

            target.TableIds = original.TableIds.ToList();
            target.Capacity = original.Capacity;
            target.AssignedWaiterId = original.AssignedWaiterId;
            target.ManuallyAssigned = original.ManuallyAssigned;
            target.MergedFrom = null;
            target.OccupiedBy = null;
            target.RecentJokeCount = 0;
            target.AnchorPosition = floor.Tables.First(t => t.TableId == original.TableIds.Min()).Position;

            if (!ReferenceEquals(target, cluster))
                floor.Clusters.Add(target);

            foreach (var table in floor.Tables.Where(t => original.TableIds.Contains(t.TableId)))
                table.ClusterId = original.ClusterId;

            restored.Add(new ClusterRestored(original.ClusterId, original.TableIds.ToList(), original.Capacity));
        }

        await _repository.SaveAsync(restaurant, cancellationToken);
        await _publisher.Publish(
            new ClusterSplit(restaurant.RestaurantId, request.ClusterId, restored, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
