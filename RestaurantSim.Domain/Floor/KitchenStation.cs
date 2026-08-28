namespace RestaurantSim.Domain.Floor;

public sealed class KitchenStationType
{
    public required string StationTypeId { get; init; }
    public required int Capacity { get; init; }
    public required float SpeedMultiplier { get; init; }
}

public sealed class KitchenStation : IPlaceable
{
    public required int StationId { get; init; }
    public required GridPosition Position { get; init; }
    public required GridBounds Hitbox { get; init; }
    public IReadOnlyList<GridPosition>? OccupiedCells { get; init; }

    public required KitchenStationType StationType { get; init; }

    // OrderItemIds currently cooking here; full when Count >= StationType.Capacity.
    public List<int> OccupiedSlots { get; init; } = new();
}
