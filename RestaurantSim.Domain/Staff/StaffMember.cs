using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Domain.Staff;

public enum StaffRole
{
    Waiter,
    Host,
    Cook,
    HeadChef,
    Busser
}

public enum StaffState
{
    Idle,
    Busy,

    // Reserved for future stamina/break DLC; unused in MVP.
    Resting,

    // Outside the staff member's full daily shift span (entity doc §24a).
    OffShift
}

public enum TaskType
{
    SeatGroup,
    CleanTable,
    CleanBathroom,
    DeliverOrderItem,
    AcknowledgeCall,
    CookItem
}

public sealed class StaffMember
{
    public required int StaffId { get; init; }
    public required string Name { get; set; }

    // Behavior resolved via IStaffBehavior[Role] dispatch, not inheritance.
    public required StaffRole Role { get; init; }
    public StaffState State { get; set; } = StaffState.OffShift;

    // Both null or both populated — the target interpretation depends on CurrentTaskType
    // (TableId for CleanTable, BathroomId for CleanBathroom, OrderItemId for
    // DeliverOrderItem/CookItem, CallId for AcknowledgeCall, GroupId for SeatGroup).
    public TaskType? CurrentTaskType { get; set; }
    public int? CurrentTaskTargetId { get; set; }

    public required StaffStats Stats { get; init; }

    // Where the staff member stands NOW. For now (no movement yet) always the idle spot the
    // layout gave them; movement will change it. The first value it is given is also the
    // HomePosition (same pattern as Table, entity doc §3): where they go back to when idle.
    public GridPosition Position
    {
        get => _position;
        set
        {
            _homePosition ??= value;
            _position = value;
        }
    }

    public GridPosition HomePosition
    {
        get => _homePosition ?? _position;
        init => _homePosition = value;
    }

    private GridPosition _position;
    private GridPosition? _homePosition;

    // References MealPeriodDefinition.MealPeriodId; validated as a subset of
    // Restaurant's own ActiveMealPeriods at assignment/hire time.
    public required IReadOnlyList<string> AssignedMealPeriods { get; set; }
}
