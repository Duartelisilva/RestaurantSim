namespace RestaurantSim.Domain.Floor;

public sealed class Table : IPlaceable
{
    public required int TableId { get; init; }

    // Where the table is NOW. Staff slide tables together for a merge and back afterwards,
    // so this changes at runtime. The first value it is given (layout load) is also its
    // HomePosition; later moves never touch HomePosition.
    public required GridPosition Position
    {
        get => _position;
        set
        {
            _homePosition ??= value;
            _position = value;
        }
    }

    // The layout default; a split puts the table back here. Never null once the table
    // exists, so it is exposed non-nullable (it can also be set explicitly at creation).
    public GridPosition HomePosition
    {
        get => _homePosition ?? _position;
        init => _homePosition = value;
    }

    // Set only while the table is sliding (merge or return): where it started, and when it
    // arrives. A table that is moving makes its whole cluster unusable (TableClusterState.Moving).
    public GridPosition? MoveFrom { get; set; }
    public long? MoveEndTick { get; set; }
    public bool IsMoving => MoveEndTick is not null;

    private GridPosition _position;
    private GridPosition? _homePosition;
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
