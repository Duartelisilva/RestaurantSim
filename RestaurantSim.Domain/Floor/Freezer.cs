namespace RestaurantSim.Domain.Floor;

// Passive pickup fixture (entity doc §18a). No stock/capacity fields —
// those arrive additively with Inventory DLC.
public sealed class Freezer : IPlaceable
{
    public required int FreezerId { get; init; }
    public required GridPosition Position { get; init; }
    public required GridBounds Hitbox { get; init; }
    public IReadOnlyList<GridPosition>? OccupiedCells { get; init; }
}
