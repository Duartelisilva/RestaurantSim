namespace RestaurantSim.Contracts.Snapshot;

// Wire names of the staff enums. Contracts has no reference to Domain, so these strings are
// the contract; a test in Application.Tests pins them to the Domain enums' member names.
public static class StaffRoleNames
{
    public const string Waiter = "Waiter";
    public const string Host = "Host";
    public const string Cook = "Cook";
    public const string HeadChef = "HeadChef";
    public const string Busser = "Busser";
}

public static class StaffStateNames
{
    public const string Idle = "Idle";
    public const string Busy = "Busy";
    public const string Resting = "Resting";
    public const string OffShift = "OffShift";
}

public static class TaskTypeNames
{
    public const string SeatGroup = "SeatGroup";
    public const string CleanTable = "CleanTable";
    public const string CleanBathroom = "CleanBathroom";
    public const string DeliverOrderItem = "DeliverOrderItem";
    public const string AcknowledgeCall = "AcknowledgeCall";
    public const string CookItem = "CookItem";
}
