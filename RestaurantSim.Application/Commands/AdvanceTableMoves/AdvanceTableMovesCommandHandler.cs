using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Application.Commands.AdvanceTableMoves;

public sealed class AdvanceTableMovesCommandHandler : IRequestHandler<AdvanceTableMovesCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPublisher _publisher;

    public AdvanceTableMovesCommandHandler(IRestaurantRepository repository, IPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(AdvanceTableMovesCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var arrived = restaurant.Floor.Tables
            .Where(t => t.MoveEndTick is { } end && end <= restaurant.SimulationTick)
            .ToList();

        if (arrived.Count == 0)
            return Result.Success();

        foreach (var table in arrived)
            table.CompleteMove();

        await _repository.SaveAsync(restaurant, cancellationToken);

        var clusterIds = arrived
            .Select(t => t.ClusterId)
            .OfType<int>()
            .Distinct()
            .Order();

        foreach (var clusterId in clusterIds)
            await _publisher.Publish(new TableMoveCompleted(restaurant.RestaurantId, clusterId, restaurant.SimulationTick), cancellationToken);

        return Result.Success();
    }
}
