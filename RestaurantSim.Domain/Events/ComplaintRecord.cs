namespace RestaurantSim.Domain.Events;

public enum ComplaintCategory
{
    LongWait,
    WrongOrder,
    FoodQuality,
    Rudeness,
    Cleanliness,
    Other
}

public sealed class ComplaintRecord
{
    public required int ComplaintId { get; init; }
    public required int GroupId { get; init; }
    public required ComplaintCategory Category { get; init; }

    // Derived at creation: Category base weight x filer's Mood/Instability (mechanics doc §3).
    public required float Severity { get; init; }
    public required long FiledAtTick { get; init; }
}
