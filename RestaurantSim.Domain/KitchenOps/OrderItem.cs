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
    public long? CookingStartedAtTick { get; set; }
    public long? PlannedStartTick { get; set; }
    public long? HeldSinceTick { get; set; }
    public bool IsHighQuality { get; set; }
    public bool HadFoodQualityEvent { get; set; }

    // Service flow (mechanics doc §17, draft): when the person finishes eating/drinking
    // this item — rolled when it is Served. Null until then.
    public long? ConsumedByTick { get; set; }

    // New — needed to gate pickup-item (Drinks) fetch time. Order.CreatedAtTick
    // reflects only the FIRST item ever added to the order, which is wrong for
    // a later mid-meal drink refill added to an already-existing order.
    public required long CreatedAtTick { get; init; }
}

public sealed class Order
{
    public required int OrderId { get; init; }
    public required int GroupId { get; init; }
    public required int ClusterId { get; init; }
    public required List<OrderItem> Items { get; init; }
    public required long CreatedAtTick { get; init; }
}
