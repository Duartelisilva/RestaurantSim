using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Infrastructure.Presets;

// MVP stand-in for architecture doc §9's JSON-on-disk loader. Lets every
// consumer depend on the real IPresetRepository<T> abstraction now; swapping
// to a JsonPresetRepository<T> later (once presets are actual JSON files) is
// a pure Infrastructure change, zero handler edits.
public sealed class InMemoryPresetRepository<T> : IPresetRepository<T>
{
    private readonly IReadOnlyDictionary<string, T> _presets;

    public InMemoryPresetRepository(IReadOnlyDictionary<string, T> presets)
    {
        _presets = presets;
    }

    public Task<T?> GetAsync(string presetId, CancellationToken cancellationToken = default)
    {
        _presets.TryGetValue(presetId, out var preset);
        return Task.FromResult(preset);
    }
}
