using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Application.Queries.GetRestaurantSnapshot;

public sealed class GetRestaurantSnapshotQueryHandler
    : IRequestHandler<GetRestaurantSnapshotQuery, Result<RestaurantSnapshotDto>>
{
    private readonly IRestaurantRepository _repository;

    public GetRestaurantSnapshotQueryHandler(IRestaurantRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<RestaurantSnapshotDto>> Handle(
        GetRestaurantSnapshotQuery request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result<RestaurantSnapshotDto>.Failure($"Restaurant {request.RestaurantId} does not exist.");

        return Result<RestaurantSnapshotDto>.Success(RestaurantSnapshotMapper.Map(restaurant));
    }
}
