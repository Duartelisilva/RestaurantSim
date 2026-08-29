namespace RestaurantSim.Domain.Abstractions;

// Architecture doc §9: presets are JSON-on-disk, loaded via this abstraction.
// Not previously built — Arrival is the first system that actually needs
// RestaurantConfig data at runtime.
public interface IPresetRepository<T>
{
    Task<T?> GetAsync(string presetId, CancellationToken cancellationToken = default);
}
