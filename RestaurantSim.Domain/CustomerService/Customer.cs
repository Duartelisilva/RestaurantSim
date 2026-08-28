namespace RestaurantSim.Domain.CustomerService;

public enum Gender
{
    Male,
    Female
}

public enum SpendingTendency
{
    Budget,
    Normal,
    Generous
}

public sealed class Customer
{
    public required int CustomerId { get; init; }

    // Physical seat, null until seated — pairs with Seat.OccupiedBy for order routing.
    public int? SeatIndex { get; set; }

    // Gates calling, paying, and Kids-menu-only ordering.
    public required bool IsAdult { get; init; }

    // Dynamic (0-100). Read on every complaint check and at payment (tip calc).
    public float Mood { get; set; }

    // Modifier on how fast this individual's Mood rises/falls.
    public required float PatienceLevel { get; init; }

    // Fixed for the visit. Severity/threshold multiplier on complaint rolls.
    public required float Instability { get; init; }

    // Per-person pull toward group-average Mood (intra-group spread).
    public required float SusceptibilityToGroup { get; init; }

    public required SpendingTendency SpendingTendency { get; init; }
    public required bool IsRegular { get; init; }

    // True while seated; false while away (bathroom, etc.). Mood is frozen while false.
    public bool IsPresentAtTable { get; set; } = true;

    // Routes which gendered Bathroom queue this customer joins (mechanics doc §12).
    public required Gender Gender { get; init; }

    // Feeds the logarithmic decay in the go-to-bathroom roll (mechanics doc §12).
    public int BathroomVisitCount { get; set; }

    // Gates the per-visit cooldown before another trip is eligible.
    public long? LastBathroomVisitEndTick { get; set; }
}
