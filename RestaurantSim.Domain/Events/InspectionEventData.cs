namespace RestaurantSim.Domain.Events;

public sealed class InspectionEventData
{
    public string? GradeResult { get; set; }
    public decimal? FineAmount { get; set; }

    // Genuine processing time, derived from InspectionDurationSeconds (mechanics doc §14).
    public required long DurationTicks { get; init; }
}
