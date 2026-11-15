using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.MergeClusters;

public sealed class MergeClustersCommandHandler : IRequestHandler<MergeClustersCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IPublisher _publisher;

    public MergeClustersCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(MergeClustersCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var floor = restaurant.Floor;

        foreach (var id in request.ClusterIds)
        {
            if (floor.Clusters.All(c => c.ClusterId != id))
                return Result.Failure($"Cluster {id} does not exist.");
        }

        // The single definition of "these can merge": CanMerge, axis, close enough with room
        // to slide, all free.
        var geometry = MergeGeometry.For(floor);
        if (!TableMergeRules.IsValidMerge(request.ClusterIds, floor.Tables, floor.Clusters, FloorQueries.FreeClusterIds(restaurant), geometry))
            return Result.Failure("These clusters cannot be merged (they must be free, in one line, close enough and with room to push together).");

        var plan = TableMergeRules.PlanMerge(
            request.ClusterIds, floor.Tables, floor.Clusters, WaiterWorkload.SeatedGroupsByWaiter(restaurant), geometry);
        if (plan is null)
            return Result.Failure("These clusters cannot be merged (there is no way to push them together).");

        // ---- everything checked; commit ----
        var durationTicks = TableMoveTiming.DurationTicks(plan.Moves, config);
        var arrivalTick = restaurant.SimulationTick + durationTicks;

        foreach (var move in plan.Moves)
            floor.Tables.First(t => t.TableId == move.TableId).StartMove(move.To, arrivalTick);

        var parts = request.ClusterIds
            .Select(id => floor.Clusters.First(c => c.ClusterId == id))
            .OrderBy(c => c.ClusterId)
            .ToList();

        var survivor = parts[0];
        var tableIds = parts.SelectMany(c => c.TableIds).OrderBy(id => id).ToList();

        // Always remember the ORIGINAL clusters, even when merged clusters are merged again.
        var composition = parts
            .SelectMany(c => c.MergedFrom
                ?? new[] { new ClusterComposition(c.ClusterId, c.TableIds.ToList(), c.Capacity, c.AssignedWaiterId, c.ManuallyAssigned) })
            .ToList();

        survivor.TableIds = tableIds;
        survivor.Capacity = parts.Sum(c => c.Capacity);
        survivor.MergedFrom = composition;
        survivor.ManuallyAssigned = false;

        // The least busy waiter among the merged clusters' own waiters; only if none of them
        // has one does it fall back to the least busy waiter on shift.
        survivor.AssignedWaiterId = plan.WaiterId ?? WaiterWorkload.LeastLoadedWaiterId(restaurant) ?? survivor.AssignedWaiterId;
        survivor.AnchorPosition = floor.Tables.First(t => t.TableId == tableIds[0]).Position;

        foreach (var table in floor.Tables.Where(t => tableIds.Contains(t.TableId)))
            table.ClusterId = survivor.ClusterId;

        foreach (var absorbed in parts.Skip(1))
            floor.Clusters.Remove(absorbed);

        await _repository.SaveAsync(restaurant, cancellationToken);
        await _publisher.Publish(
            new ClustersMerged(restaurant.RestaurantId, survivor.ClusterId, parts.Select(c => c.ClusterId).ToList(),
                tableIds, survivor.Capacity, plan.Moves, durationTicks, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
