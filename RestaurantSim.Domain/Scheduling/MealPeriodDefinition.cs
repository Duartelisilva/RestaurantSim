namespace RestaurantSim.Domain.Scheduling;

public readonly record struct TimeOfDay(int Hour, int Minute);

// Preset key (e.g. "breakfast", "lunch", "afternoon", "dinner") — same pattern
// as TableShape.ShapeId/KitchenStationType.StationTypeId. A DLC period is a new
// preset entry only, no enum member, no recompiled assembly (entity doc §24a).
public sealed class MealPeriodDefinition
{
    public required string MealPeriodId { get; init; }
    public required TimeOfDay StartTime { get; init; }
    public required TimeOfDay EndTime { get; init; }
}
