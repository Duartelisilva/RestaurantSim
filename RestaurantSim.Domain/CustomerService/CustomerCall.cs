namespace RestaurantSim.Domain.CustomerService;

public enum CustomerCallType
{
    RequestNextCourse,
    RequestItem,
    RequestBill
}

public enum CustomerCallStatus
{
    Pending,
    Acknowledged,
    Resolved
}

public sealed class CustomerCall
{
    public required int CallId { get; init; }

    // Table-level, not customer-level — any adult member can trigger.
    public required int ClusterId { get; init; }
    public required CustomerCallType Type { get; init; }
    public CustomerCallStatus Status { get; set; } = CustomerCallStatus.Pending;

    // Capped by RestaurantConfig.MaxCallAttempts (default 3).
    public int AttemptCount { get; set; } = 1;

    public required long CalledAtTick { get; init; }
    public long? AcknowledgedAtTick { get; set; }
}
