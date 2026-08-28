namespace RestaurantSim.Domain.CustomerService;

public enum GroupLifecycleState
{
    Arriving,
    Queued,
    Seated,
    Leaving
}

public enum CoursePhase
{
    Seated,
    Drinks,
    EntryMain,
    Dessert,
    Bill,
    Done
}

public enum GroupPhaseWaitState
{
    AutoVisitPending,
    Serving,
    AwaitingDecision,
    RecheckScheduled
}

public sealed class CustomerGroup
{
    public required int GroupId { get; init; }

    // 1-6, skewed toward 2.
    public required int PartySize { get; init; }
    public required IReadOnlyList<Customer> Members { get; init; }

    // Assigned TableCluster; null while queued.
    public int? ClusterId { get; set; }
    public int? ReservationId { get; set; }

    public GroupLifecycleState LifecycleState { get; set; } = GroupLifecycleState.Arriving;
    public required long ArrivalTick { get; init; }

    // Table-wide — whole table always shares one phase.
    public CoursePhase CoursePhase { get; set; } = CoursePhase.Seated;
    public GroupPhaseWaitState PhaseWaitState { get; set; } = GroupPhaseWaitState.AutoVisitPending;
    public long NextAutoVisitTick { get; set; }

    public int? CurrentOrderId { get; set; }

    // Designated bill payer — MVP: first adult member (CustomerId).
    public int? PayerId { get; set; }

    // Extension pattern (entity doc §21): open-ended tag, e.g. "critic", "celebrity".
    // Inert to core logic — never branched on outside owning DLC module.
    public string? SpecialRole { get; set; }
    public Dictionary<string, float>? SpecialRoleData { get; set; }
}
