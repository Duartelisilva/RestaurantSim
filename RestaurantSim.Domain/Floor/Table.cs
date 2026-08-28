namespace RestaurantSim.Domain.Floor;

public sealed class Table : IPlaceable
{
    public required int TableId { get; init; }

    // Fixed for MVP (no player rearranging); DLC move-mode only ever touches these two.
    public required GridPosition Position { get; set; }
    public required GridBounds Hitbox { get; init; }
    public IReadOnlyList<GridPosition>? OccupiedCells { get; init; }

    public required TableShape Shape { get; init; }
    public required IReadOnlyList<Seat> Seats { get; init; }

    // Which TableCluster this table currently belongs to; null if standalone.
    public int? ClusterId { get; set; }

    // Set false when a group leaves; Busser cleaning (mechanics doc §13) flips it true.
    public bool IsClean { get; set; } = true;
}
