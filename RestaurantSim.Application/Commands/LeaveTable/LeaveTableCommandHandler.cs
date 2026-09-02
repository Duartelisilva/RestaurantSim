using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Application.Commands.LeaveTable;

public sealed class LeaveTableCommandHandler : IRequestHandler<LeaveTableCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPublisher _publisher;

    public LeaveTableCommandHandler(IRestaurantRepository repository, IPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(LeaveTableCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == request.GroupId);
        if (group is null)
            return Result.Failure($"Group {request.GroupId} is not currently active.");

        if (group.LifecycleState != GroupLifecycleState.Leaving)
            return Result.Failure($"Group {request.GroupId} is not currently in a Leaving state (must pay first).");

        if (group.ClusterId is null)
            return Result.Failure($"Group {request.GroupId} has no assigned table — data integrity issue.");

        var cluster = restaurant.Floor.Clusters.FirstOrDefault(c => c.ClusterId == group.ClusterId);
        if (cluster is null)
            return Result.Failure($"Cluster {group.ClusterId} does not exist.");

        // Free the cluster and dirty each member table (business logic §1.3) —
        // a Busser must clean each one before it's usable again. TableCluster.
        // State is derived (entity doc §5), so this alone is enough for the
        // cluster to correctly report NeedsCleaning next time it's queried.
        cluster.OccupiedBy = null;
        foreach (var tableId in cluster.TableIds)
        {
            var table = restaurant.Floor.Tables.FirstOrDefault(t => t.TableId == tableId);
            if (table is not null)
                table.IsClean = false;
        }

        var finalAverageMood = group.Members.Count > 0 ? group.Members.Average(m => m.Mood) : 0f;

        // Group is now fully gone — no "Left"/"Gone" LifecycleState value exists
        // (entity doc §8 only has Arriving/Queued/Seated/Leaving), so removal
        // from ActiveGroups IS the terminal step, matching entity doc §21's own
        // "LifecycleState reaches Leaving/gone" phrasing for event resolution.
        restaurant.CustomerService.ActiveGroups.Remove(group);

        // The group's Order is no longer active either. No archival/history
        // mechanism exists yet (ISnapshotStore isn't built) — this data is lost
        // once removed, not persisted for stats. Flagged in pending-doc-changes.
        if (group.CurrentOrderId is int orderId)
        {
            var order = restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == orderId);
            if (order is not null)
                restaurant.KitchenOps.ActiveOrders.Remove(order);
        }

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(
            new GroupLeft(restaurant.RestaurantId, group.GroupId, cluster.ClusterId, (float)finalAverageMood, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
