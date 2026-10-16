using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Application.Queries.GetRestaurantSnapshot;

public sealed class GetRestaurantSnapshotQueryHandler
    : IRequestHandler<GetRestaurantSnapshotQuery, Result<RestaurantSnapshotDto>>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;

    public GetRestaurantSnapshotQueryHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository)
    {
        _repository = repository;
        _presetRepository = presetRepository;
    }

    public async Task<Result<RestaurantSnapshotDto>> Handle(
        GetRestaurantSnapshotQuery request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result<RestaurantSnapshotDto>.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result<RestaurantSnapshotDto>.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        return Result<RestaurantSnapshotDto>.Success(RestaurantSnapshotMapper.Map(restaurant, config));
    }
}
