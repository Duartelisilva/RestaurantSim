namespace RestaurantSim.Contracts.Events;

// A staff member's resulting state (names in Contracts.Snapshot.StaffStateNames / TaskTypeNames).
// TaskType and TaskTargetId are both null when there is no task. Absolute, so applying it
// twice is harmless; Tick as in GroupSeatedDto.
public sealed record StaffStateChangedDto(
    int StaffId,
    string State,
    string? TaskType,
    int? TaskTargetId,
    float Fatigue,
    long Tick);
