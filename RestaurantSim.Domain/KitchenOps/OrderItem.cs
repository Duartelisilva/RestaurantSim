namespace RestaurantSim.Domain.KitchenOps;

public enum OrderItemStatus
{
    Queued,
    Cooking,
    HeldUnderLamp,
    Ready,
    Served
}

// One record per unit of food/drink — no Quantity field. Status is per-record,
// so a Quantity > 1 item couldn't represent one unit Ready while another Cooking.
public sealed class OrderItem
{
    public required int OrderItemId { get; init; }

    // CustomerId — enables exact per-person food routing via Seat.OccupiedBy.
    public required int OwnerId { get; init; }
    public required int MenuItemId { get; init; }
    public required ItemPhase Phase { get; init; }

    // Snapshot of MenuItem.Price at order time.
    public required decimal PriceAtOrder { get; init; }
    public OrderItemStatus Status { get; set; } = OrderItemStatus.Queued;

    // Which KitchenTicket batch this item is scheduled under.
    public int? TicketId { get; set; }

    // Which KitchenStation it's cooking at, once Status == Cooking.
    public int? StationId { get; set; }

    // Set when Status becomes HeldUnderLamp — basis for QualityAtServe.
    public long? HeldSinceTick { get; set; }

    // Rolled once at the Cooking -> Ready transition (mechanics doc §10).
    public bool IsHighQuality { get; set; }
}

public sealed class Order
{
    public required int OrderId { get; init; }
    public required int GroupId { get; init; }

    // Redundant with the group's own ClusterId — avoids a join on the hot kitchen-scan path.
    public required int ClusterId { get; init; }

    // Accumulates across all course phases — one running order per group, not per phase.
    public required List<OrderItem> Items { get; init; }
    public required long CreatedAtTick { get; init; }
}
