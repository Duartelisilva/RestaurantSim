namespace RestaurantSim.Domain.KitchenOps;

// The sync-batch unit: items submitted together (same phase, same table)
// that the kitchen schedules to finish together.
public sealed class KitchenTicket
{
    public required int TicketId { get; init; }
    public required int ClusterId { get; init; }
    public required IReadOnlyList<int> ItemIds { get; init; }
    public required long SubmittedAtTick { get; init; }

    // Computed sync target — the scheduling algorithm's output (mechanics doc §10).
    public long TargetReadyTick { get; set; }

    // Player-settable priority bump via PrioritizeTicketCommand (mechanics doc §10).
    public bool PlayerPrioritized { get; set; }

    // No stored State field — ticket-level status is derived from its items' statuses
    // in a later phase, avoiding a second source of truth.
}
