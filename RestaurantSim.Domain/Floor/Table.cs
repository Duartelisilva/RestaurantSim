namespace RestaurantSim.Domain.Floor;

public sealed class Table : IPlaceable
{
    public required int TableId { get; init; }
    public required GridPosition Position { get; set; }
    public required GridBounds Hitbox { get; init; }
    public IReadOnlyList<GridPosition>? OccupiedCells { get; init; }
    public required TableShape Shape { get; init; }
    public required IReadOnlyList<Seat> Seats { get; init; }
    public int? ClusterId { get; set; }
    public bool IsClean { get; set; } = true;

    // New — mechanics doc §13's cleaning formula produces a 0-100 progress
    // value; IsClean alone (a bool) has nowhere to store it. Persists across
    // interruptions per the doc's own "progress persists regardless of
    // interruption direction" rule — simply not resetting it is sufficient,
    // since it lives here rather than on the Busser.
    public float CleaningProgress { get; set; }
}
