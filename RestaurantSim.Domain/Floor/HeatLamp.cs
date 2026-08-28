namespace RestaurantSim.Domain.Floor;

public sealed class HeatLamp : IPlaceable
{
    public required int HeatLampId { get; init; }
    public required GridPosition Position { get; init; }
    public required GridBounds Hitbox { get; init; }
    public IReadOnlyList<GridPosition>? OccupiedCells { get; init; }

    public required int SlotCount { get; init; }

    // OrderItemIds currently held; full when Count >= SlotCount.
    public List<int> OccupiedSlots { get; init; } = new();
    public required float QualityDecayRatePerTick { get; init; }
}
