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

    // Type-specific payload — differs meaningfully
    // per type, not one bag of nullable fields shared by all.
    public object? Data { get; set; }
}
