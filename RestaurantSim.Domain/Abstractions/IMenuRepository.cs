using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Domain.Abstractions;

// Dedicated interface rather than reusing IPresetRepository<T> — MenuItem.MenuItemId
// is int, not the string-key pattern every other preset type follows (TableShape,
// KitchenStationType, MealPeriodDefinition, RestaurantConfig). That's an existing
// entity-doc inconsistency, not something introduced here.
public interface IMenuRepository
{
    Task<MenuItem?> GetByIdAsync(int menuItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuItem>> GetAllAsync(CancellationToken cancellationToken = default);
}
