using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Infrastructure.Presets;

// Stores FACTORIES, not pre-built instances — every call produces a fresh
// RestaurantLayout so two restaurants created from the same layout never
// share mutable object references.
public sealed class InMemoryLayoutRepository : ILayoutRepository
{
    private readonly IReadOnlyDictionary<string, Func<RestaurantLayout>> _factories;

    public InMemoryLayoutRepository(IReadOnlyDictionary<string, Func<RestaurantLayout>> factories)
    {
        _factories = factories;
    }

    public Task<RestaurantLayout?> GetByIdAsync(string layoutId, CancellationToken cancellationToken = default)
    {
        var layout = _factories.TryGetValue(layoutId, out var factory) ? factory() : null;
        return Task.FromResult(layout);
    }
}
