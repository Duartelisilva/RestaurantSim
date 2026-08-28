namespace RestaurantSim.Domain.Floor;

public interface IPlaceable
{
    GridPosition Position { get; }
    GridBounds Hitbox { get; }

    // Null = fully solid rectangle per Hitbox (default for every current MVP entity).
    // Populated only by future non-rectangular DLC placeables.
    IReadOnlyList<GridPosition>? OccupiedCells { get; }
}
