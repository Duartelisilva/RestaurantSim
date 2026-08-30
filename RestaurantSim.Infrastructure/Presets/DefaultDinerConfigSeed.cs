using RestaurantSim.Domain;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Infrastructure.Presets;

// Temporary C# stand-in for the real JSON preset (architecture doc §9) —
// transcribes restaurant-simulator-presets.md's "default_diner" config directly,
// per CLAUDE.md's "convert directly to JSON... don't re-derive values." Swap for
// a JsonPresetRepository<RestaurantConfig> reading an actual JSON file later;
// this exists only so Arrival (and future systems) have real config data now.
public static class DefaultDinerConfigSeed
{
    public static RestaurantConfig Create() => new()
    {
        ConfigId = "default_diner",
        StartingCash = 5000m,
        MinutesPerTick = 1,
        TicksPerRealSecond = 1,
        TableLayoutId = "default_diner_layout",
        StaffRosterSeed = new (StaffRole Role, int Count)[]
        {
            (StaffRole.Host, 1),
            (StaffRole.Waiter, 2),
            (StaffRole.HeadChef, 1),
            (StaffRole.Cook, 2),
            (StaffRole.Busser, 1)
        },
        MenuItemIds = Enumerable.Range(1, 16).ToArray(),
        ReputationStart = 20f,
        PatienceDecayBaseline = null, // open, pending playtesting (presets doc)
        QueueWaitMoodDecayRate = -0.4f,
        CourseWaitMoodDecayRate = -0.3f,
        NaturalMoodRecoveryRate = 0.5f,
        ProbabilityCheckpointIntervalSeconds = 3f,
        PathReplanMaxWaitSeconds = 5f,
        MaxCallAttempts = 3,
        ActiveMealPeriods = new List<MealPeriodDefinition>
        {
            new() { MealPeriodId = "lunch", StartTime = new TimeOfDay(12, 0), EndTime = new TimeOfDay(15, 0) },
            new() { MealPeriodId = "dinner", StartTime = new TimeOfDay(19, 0), EndTime = new TimeOfDay(0, 0) }
        },
        EventModuleIds = Array.Empty<string>(),
        ShortDelayThresholdSeconds = null,
        MaxAcceptableWaitSeconds = null,
        BaseHighQualityChance = null,
        BaseEventChance = null,
        PlatingTimeSeconds = null,
        TipBaseRate = 50f,
        TipMoodExponent = 0.75f,
        AutoJokeCooldownSeconds = null,
        MoodGapThreshold = null,
        FacilitiesTimeSecondsMale = null,
        FacilitiesTimeSecondsFemale = null,
        BaseBathroomVisitChance = null,
        BathroomCooldownSeconds = null,
        EatingMealtimeVisitModifier = null,
        WashHandsTimeSecondsMale = null,
        WashHandsTimeSecondsFemale = null,
        AlmostDirtyThreshold = null,
        DirtyThreshold = null,
        TableCleaningBaseSeconds = null,
        BathroomCleaningBaseSeconds = null,
        ComplaintCategoryWeights = null,
        InspectionDurationSeconds = null,

        // New fields — not yet in entity doc §22, added specifically to make
        // Arrival implementable. Flag for backport into the locked doc once approved.
        MaxQueueLength = 12,             // first-pass proposed default
        BaseArrivalIntervalSeconds = 120f // first-pass proposed default (2 min at MVP tick rate)
    };
}
