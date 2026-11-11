namespace RestaurantSim.Domain.Floor;

// The cells a placeable covers (entity doc §1): OccupiedCells (relative to Position) when a
// non-rectangular object supplies them, otherwise every cell inside Hitbox — one fallback
// rule for everything that needs collision (mechanics doc §9).
public static class Footprint
{
    public static IEnumerable<GridPosition> Cells(IPlaceable placeable) =>
        Cells(placeable.Position, placeable.Hitbox, placeable.OccupiedCells);

    public static IEnumerable<GridPosition> Cells(
        GridPosition position, GridBounds hitbox, IReadOnlyList<GridPosition>? occupiedCells)
    {
        if (occupiedCells is not null)
            return occupiedCells.Select(c => new GridPosition(position.X + c.X, position.Y + c.Y));

        return Rectangle(position, hitbox);
    }

    private static IEnumerable<GridPosition> Rectangle(GridPosition position, GridBounds hitbox)
    {
        for (var dx = 0; dx < hitbox.Width; dx++)
            for (var dy = 0; dy < hitbox.Height; dy++)
                yield return new GridPosition(position.X + dx, position.Y + dy);
    }
}
