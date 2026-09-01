namespace RestaurantSim.Domain.KitchenOps;

public sealed class KitchenTicket
{
    public required int TicketId { get; init; }
    public required int ClusterId { get; init; }
    public required IReadOnlyList<int> ItemIds { get; init; }
    public required long SubmittedAtTick { get; init; }

    // Nominal sync target, computed at creation (mechanics doc §10) — see
    // OrderItem.PlannedStartTick for how per-item targets derive from this.
    public long TargetReadyTick { get; set; }

    public bool PlayerPrioritized { get; set; }

    // New — not in entity doc §14. Implements "once re-informed, every later
    // item on this ticket is served immediately on Ready, no further
    // hold-evaluation per item" (mechanics doc §10). Set true the first time
    // any item on this ticket triggers re-inform.
    public bool IsAsyncMode { get; set; }
}
