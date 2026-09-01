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

    // New — needed to compute QualityAtServe (mechanics doc §10) at delivery
    // time. Previously rolled in AdvanceKitchenCommandHandler and discarded;
    // now stored so DeliverOrderItemCommand can actually use it.
    public bool HadFoodQualityEvent { get; set; }
}

public sealed class Order
{
    public required int OrderId { get; init; }
    public required int GroupId { get; init; }
    public required int ClusterId { get; init; }
    public required List<OrderItem> Items { get; init; }
    public required long CreatedAtTick { get; init; }
}
