using RestaurantSim.Domain;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Tests.Support;

// A minimal RestaurantConfig for handlers that only care about a couple of its values.
public static class TestRestaurantConfig
{
    public static RestaurantConfig Build(float? secondsPerCell = null, int ticksPerSecond = 1, int? maxMergeGapCells = null) => new()
    {
        ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = ticksPerSecond,
        TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(), MenuItemIds = new[] { 1 },
        ReputationStart = 20f, QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f,
        NaturalMoodRecoveryPerSecond = 0.5f, ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f,
        MaxCallAttempts = 3, ActiveMealPeriods = new List<MealPeriodDefinition>(), EventModuleIds = Array.Empty<string>(),
        TipBaseRate = 50f, TipMoodExponent = 0.75f, MaxQueueLength = 12, BaseArrivalIntervalSeconds = 15f,
        TableMoveSecondsPerCell = secondsPerCell, MaxMergeGapCells = maxMergeGapCells
    };
}
