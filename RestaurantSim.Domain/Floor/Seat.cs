using System.Numerics;

namespace RestaurantSim.Domain.Floor;

public sealed class Seat
{
    public required int SeatIndex { get; init; }
    public required Vector2 RelativeOffset { get; init; }
    public required Orientation Orientation { get; init; }

    // CustomerId of the occupant, null if empty.
    public int? OccupiedBy { get; set; }
}
