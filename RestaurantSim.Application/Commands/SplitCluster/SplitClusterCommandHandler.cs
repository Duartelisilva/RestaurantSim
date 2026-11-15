using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Application.Commands.SplitCluster;

public sealed class SplitClusterCommandHandler : IRequestHandler<SplitClusterCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IPublisher _publisher;

    public SplitClusterCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(SplitClusterCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var floor = restaurant.Floor;
        var cluster = floor.Clusters.FirstOrDefault(c => c.ClusterId == request.ClusterId);
        if (cluster is null)
            return Result.Failure($"Cluster {request.ClusterId} does not exist.");

        if (cluster.MergedFrom is not { Count: > 0 } composition)
            return Result.Failure($"Cluster {request.ClusterId} was not created by a merge and cannot be split.");

        // Free also rules out a cluster that is still sliding into place (Moving).
        if (!FloorQueries.FreeClusterIds(restaurant).Contains(cluster.ClusterId))
            return Result.Failure($"Cluster {request.ClusterId} must be free and clean to be split.");

        // "Check all, then commit all": every table needs its home cell free, or nothing moves.
        var clusterTables = floor.Tables.Where(t => cluster.TableIds.Contains(t.TableId)).ToList();
        var moves = TableMergeRules.PlanReturn(clusterTables, MergeGeometry.BlockedCellsOf(floor, cluster.TableIds));
        if (moves is null)
            return Result.Failure($"Cluster {request.ClusterId} cannot be split yet: a table's home position is occupied.");

        // ---- everything checked; commit ----
        var durationTicks = TableMoveTiming.DurationTicks(moves, config);
        var arrivalTick = restaurant.SimulationTick + durationTicks;

        foreach (var move in moves)
            floor.Tables.First(t => t.TableId == move.TableId).StartMove(move.To, arrivalTick);

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
            new ClusterSplit(restaurant.RestaurantId, request.ClusterId, restored, moves, durationTicks, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
