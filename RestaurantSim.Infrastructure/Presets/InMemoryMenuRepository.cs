using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Infrastructure.Presets;

public sealed class InMemoryMenuRepository : IMenuRepository
{
    private readonly IReadOnlyDictionary<int, MenuItem> _items;

    public InMemoryMenuRepository(IReadOnlyList<MenuItem> items)
    {
        _items = items.ToDictionary(i => i.MenuItemId);
    }

    public Task<MenuItem?> GetByIdAsync(int menuItemId, CancellationToken cancellationToken = default)
    {
        _items.TryGetValue(menuItemId, out var item);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<MenuItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MenuItem>>(_items.Values.ToList());
}
