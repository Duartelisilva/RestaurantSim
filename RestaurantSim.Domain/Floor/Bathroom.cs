namespace RestaurantSim.Domain.Floor;

public enum BathroomGender
{
    Male,
    Female,

    // Staff bathroom — no Mood interaction, purely a staff availability blocker.
    Unisex
}

public sealed class Bathroom : IPlaceable
{
    public required int BathroomId { get; init; }
    public required BathroomGender Gender { get; init; }

    // MVP "doors only" scope: a single door tile + one adjacent slot, no interior room.
    public required GridPosition Position { get; init; }
    public required GridBounds Hitbox { get; init; }
    public IReadOnlyList<GridPosition>? OccupiedCells { get; init; }

    public required int Capacity { get; init; }

    // CustomerId (customer bathrooms) or StaffId (unisex staff bathroom).
    public List<int> OccupiedBy { get; init; } = new();

    // Customer bathrooms only. Decays by chance on use (mechanics doc §12).
    public float CleanlinessScore { get; set; } = 100f;

    // Size-scaled default open, pending playtesting (mechanics doc §13).
    public required int MaxConcurrentCleaners { get; init; }

    // Phase 1: IsClean derivation needs RestaurantConfig.DirtyThreshold —
    // added once RestaurantConfig is wired through in a later phase.
}
