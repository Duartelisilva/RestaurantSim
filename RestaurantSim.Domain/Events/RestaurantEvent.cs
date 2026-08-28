namespace RestaurantSim.Domain.Events;

public enum RestaurantEventStatus
{
    Active,
    Resolved
}

// EventType is a preset key (e.g. "inspection", "food_critic", "celebrity"),
// not a closed enum — a DLC type is a new string value only (entity doc §21).
public sealed class RestaurantEvent
{
    public required int EventId { get; init; }
    public required string EventType { get; init; }
    public required long TriggeredAtTick { get; init; }
    public RestaurantEventStatus Status { get; set; } = RestaurantEventStatus.Active;

    // Type-specific payload (see InspectionEventData below) — differs meaningfully
    // per type, not one bag of nullable fields shared by all.
    public object? Data { get; set; }
}

// Inspection's Data payload. FoodCritic/Celebrity carry no payload — they tag a
// spawned CustomerGroup instead (entity doc §21).
public sealed class InspectionEventData
{
    public string? GradeResult { get; set; }
    public decimal? FineAmount { get; set; }

    // Genuine processing time, derived from InspectionDurationSeconds (mechanics doc §14).
    public required long DurationTicks { get; init; }
}
