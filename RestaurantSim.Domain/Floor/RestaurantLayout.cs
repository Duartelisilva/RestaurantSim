using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Domain.Floor;

// Bundles a full floor layout's worth of placeable instances, returned by
// ILayoutRepository. Kept as plain data — the repository implementation is
// responsible for returning FRESH instances on every call (two restaurants
// created from the same layout must never share the same mutable Table/
// TableCluster object).
public sealed class RestaurantLayout
{
    public required IReadOnlyList<Table> Tables { get; init; }
    public required IReadOnlyList<TableCluster> Clusters { get; init; }
    public required IReadOnlyList<KitchenStation> KitchenStations { get; init; }
    public required IReadOnlyList<HeatLamp> HeatLamps { get; init; }
    public required IReadOnlyList<Freezer> Freezers { get; init; }
    public required IReadOnlyList<Bathroom> Bathrooms { get; init; }

    // Where idle staff of each role stand, in roster order: the nth staff member of a role takes
    // the nth spot. A layout must give every staff member of the roster a spot (CreateRestaurant
    // rejects it otherwise), so an empty map is only valid for a roster with nobody in it.
    public IReadOnlyDictionary<StaffRole, IReadOnlyList<GridPosition>> StaffSpots { get; init; }
        = new Dictionary<StaffRole, IReadOnlyList<GridPosition>>();
}
