using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Application.Commands.SeatGroup;

public sealed class SeatGroupCommandHandler : IRequestHandler<SeatGroupCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPublisher _publisher;

    public SeatGroupCommandHandler(IRestaurantRepository repository, IPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(SeatGroupCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var group = restaurant.CustomerService.Queue.FirstOrDefault(g => g.GroupId == request.GroupId);
        if (group is null)
            return Result.Failure($"Group {request.GroupId} is not currently queued.");

        var cluster = restaurant.Floor.Clusters.FirstOrDefault(c => c.ClusterId == request.ClusterId);
        if (cluster is null)
            return Result.Failure($"Cluster {request.ClusterId} does not exist.");

        // Same method the validator calls — single source of truth, no
        // duplicated logic between the two layers.
        if (!cluster.CanSeat(group.PartySize, restaurant.Floor.Tables, restaurant.CustomerService.Reservations, restaurant.SimulationTick))
            return Result.Failure($"Cluster {request.ClusterId} is not available or does not fit the group.");

        restaurant.CustomerService.Queue.Remove(group);
        restaurant.CustomerService.ActiveGroups.Add(group);

        group.ClusterId = cluster.ClusterId;
        group.LifecycleState = GroupLifecycleState.Seated;
        group.CoursePhase = CoursePhase.Seated;

        cluster.OccupiedBy = group.GroupId;

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(
            new GroupSeated(restaurant.RestaurantId, group.GroupId, cluster.ClusterId, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
