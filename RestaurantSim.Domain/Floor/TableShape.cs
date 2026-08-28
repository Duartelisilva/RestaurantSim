using System.Numerics;

namespace RestaurantSim.Domain.Floor;

public enum Orientation
{
    North,
    South,
    East,
    West
}

public sealed record SeatDefinition(int SeatIndex, Vector2 RelativeOffset, Orientation Orientation);

public sealed class TableShape
{
    public required string ShapeId { get; init; }
    public required IReadOnlyList<SeatDefinition> SeatDefinitions { get; init; }

    // Hard gate checked first, before any geometry/orientation logic.
    public required bool CanMerge { get; init; }
}
