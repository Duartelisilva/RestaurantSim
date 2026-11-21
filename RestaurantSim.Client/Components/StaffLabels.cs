using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Components;

// What the UI says (and which colour class it uses) for a staff member. The state itself comes
// from the server; this only words it.
public static class StaffLabels
{
    // "idle", "cleaning table 3", "off shift" ...
    public static string Describe(StaffDto staff) => staff.State switch
    {
        StaffStateNames.OffShift => "off shift",
        StaffStateNames.Idle => "idle",
        StaffStateNames.Resting => "resting",
        StaffStateNames.Busy => Task(staff.TaskType, staff.TaskTargetId),
        _ => staff.State
    };

    // "Waiter 1 — idle · fatigue 12%" — one line for the panel and the tooltip.
    public static string Summary(StaffDto staff) =>
        $"{staff.Name} — {Describe(staff)} · fatigue {FatiguePercent(staff.Fatigue)}%";

    // Whole percent, rounded half away from zero like every other rounding in the sim; no
    // culture-dependent formatting ("12 %" vs "12%").
    private static int FatiguePercent(float fatigue) =>
        (int)Math.Round(fatigue * 100, MidpointRounding.AwayFromZero);

    // The letter(s) inside the circle on the floor.
    public static string Initial(string role) => role switch
    {
        StaffRoleNames.Waiter => "W",
        StaffRoleNames.Host => "H",
        StaffRoleNames.Cook => "C",
        StaffRoleNames.HeadChef => "HC",
        StaffRoleNames.Busser => "B",
        _ => role.Length > 0 ? role[..1] : "?"
    };

    // "role-waiter", "role-headchef" ... — styled in app.css.
    public static string RoleClass(string role) => "role-" + role.ToLowerInvariant();

    // "state-busy", "state-offshift" ...
    public static string StateClass(string state) => "staff-" + state.ToLowerInvariant();

    private static string Task(string? taskType, int? targetId) => taskType switch
    {
        TaskTypeNames.CleanTable => $"cleaning table {targetId}",
        TaskTypeNames.CleanBathroom => $"cleaning bathroom {targetId}",
        TaskTypeNames.CookItem => $"cooking item {targetId}",
        TaskTypeNames.DeliverOrderItem => $"delivering item {targetId}",
        TaskTypeNames.SeatGroup => $"seating group {targetId}",
        TaskTypeNames.AcknowledgeCall => $"answering call {targetId}",
        _ => "busy"
    };
}
