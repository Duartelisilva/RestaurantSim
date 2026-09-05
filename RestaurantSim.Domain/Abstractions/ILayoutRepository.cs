using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Domain.Abstractions;

public interface ILayoutRepository
{
    Task<RestaurantLayout?> GetByIdAsync(string layoutId, CancellationToken cancellationToken = default);
}
