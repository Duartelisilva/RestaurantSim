namespace RestaurantSim.Domain.KitchenOps;

public enum OrderItemStatus
{
    Queued,
    Cooking,
    HeldUnderLamp,
    Ready,
    Served
}

public sealed class OrderItem
{
    public required int OrderItemId { get; init; }
    public required int OwnerId { get; init; }
    public required int MenuItemId { get; init; }
    public required ItemPhase Phase { get; init; }
    public required decimal PriceAtOrder { get; init; }
    public OrderItemStatus Status { get; set; } = OrderItemStatus.Queued;
    public int? TicketId { get; set; }
    public int? StationId { get; set; }

    // New — needed to know when a Cooking item's EffectivePrepTicks have
    // elapsed (mechanics doc §10). Assigned cook is looked up via
    // StaffMember.CurrentTaskType/TargetId (entity doc §6's existing pattern).
    public long? CookingStartedAtTick { get; set; }

    // New — course-sync target (mechanics doc §10's StartTick_i), computed at
    // ticket creation using nominal baseline stats (Speed=0.5, Fatigue=0 —
    // entity doc §7's stated "average" default) since no cook is assigned yet
    // at that point. A station+cook-available item still waits until this
    // tick before starting, so faster items are deliberately held back to
    // finish alongside slower siblings — the actual locked formula, not just
    // its heat-lamp fallback.
    public long? PlannedStartTick { get; set; }

    public long? HeldSinceTick { get; set; }
    public bool IsHighQuality { get; set; }
}

public sealed class Order
{
    public required int OrderId { get; init; }
    public required int GroupId { get; init; }
    public required int ClusterId { get; init; }
    public required List<OrderItem> Items { get; init; }
    public required long CreatedAtTick { get; init; }
}
