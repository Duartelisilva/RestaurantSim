using RestaurantSim.Domain.Events;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Domain;

// The root preset — referenced by Restaurant.ConfigId, loaded once at session
// start via IPresetRepository<RestaurantConfig>. Nullable float fields below
// are the values explicitly left "open, pending playtesting" in the presets doc —
// null here means "not yet tuned," not "zero."
public sealed class RestaurantConfig
{
    public required string ConfigId { get; init; }
    public required decimal StartingCash { get; init; }
    public required int MinutesPerTick { get; init; }

    // Ticks executed per real-world second (mechanics doc §14).
    public required int TicksPerRealSecond { get; init; }

    public required string TableLayoutId { get; init; }
    public required IReadOnlyList<(StaffRole Role, int Count)> StaffRosterSeed { get; init; }
    public required IReadOnlyList<int> MenuItemIds { get; init; }
    public required float ReputationStart { get; init; }

    // Defaults to 1.0 (no change vs base curve) if left unset — mechanics doc §2.
    public float? PatienceDecayBaseline { get; init; }

    public required float QueueWaitMoodDecayRate { get; init; }
    public required float CourseWaitMoodDecayRate { get; init; }
    public required float NaturalMoodRecoveryRate { get; init; }
    public required float ProbabilityCheckpointIntervalSeconds { get; init; }
    public required float PathReplanMaxWaitSeconds { get; init; }
    public required int MaxCallAttempts { get; init; }

    // Gates Arrival spawning and staff scheduling — no active period covering
    // the current time means no arrivals and no staff clocked in.
    public required IReadOnlyList<MealPeriodDefinition> ActiveMealPeriods { get; init; }

    // New — confirmed in chat, mechanics doc §13a's formula gap. Nullable, with
    // code-level fallback defaults (derived from "~1.0 fatigue by end of a
    // 12-hour shift" and "overtime is 2x more tiring") if left unset here.
    public float? BaseFatigueAccrualRate { get; init; }
    public float? OvertimeFatigueMultiplier { get; init; }

    // Which ISimulationModules are active for this restaurant.
    public required IReadOnlyList<string> EventModuleIds { get; init; }

    // --- Open, pending playtesting (presets doc "Status" section) ---
    public float? ShortDelayThresholdSeconds { get; init; }
    public float? MaxAcceptableWaitSeconds { get; init; }
    public float? BaseHighQualityChance { get; init; }
    public float? BaseEventChance { get; init; }
    public float? PlatingTimeSeconds { get; init; }

    public required float TipBaseRate { get; init; }
    public required float TipMoodExponent { get; init; }

    public float? AutoJokeCooldownSeconds { get; init; }
    public float? MoodGapThreshold { get; init; }
    public float? FacilitiesTimeSecondsMale { get; init; }
    public float? FacilitiesTimeSecondsFemale { get; init; }
    public float? BaseBathroomVisitChance { get; init; }
    public float? BathroomCooldownSeconds { get; init; }
    public float? EatingMealtimeVisitModifier { get; init; }
    public float? WashHandsTimeSecondsMale { get; init; }
    public float? WashHandsTimeSecondsFemale { get; init; }
    public float? AlmostDirtyThreshold { get; init; }
    public float? DirtyThreshold { get; init; }
    public float? TableCleaningBaseSeconds { get; init; }
    public float? BathroomCleaningBaseSeconds { get; init; }
    public Dictionary<ComplaintCategory, float>? ComplaintCategoryWeights { get; init; }
    public float? InspectionDurationSeconds { get; init; }

    // New — not yet in entity doc §22, added to make Arrival implementable.
    public required int MaxQueueLength { get; init; }
    public required float BaseArrivalIntervalSeconds { get; init; }

    // New — not in entity doc §22. Business logic §1.2 references this threshold
    // qualitatively but never named it as a field. Nullable with a code-level
    // default fallback (15f, tied to the Mood tier table) if unset — same
    // convention as PatienceDecayBaseline.
    public float? QueueAbandonmentMoodThreshold { get; init; }

    // New — confirmed in chat, not yet in entity doc §22. Small Mood penalty
    // applied to a group's still-waiting members when their ticket flips to
    // re-inform/async mode (mechanics doc §10).
    public float? ReinformMoodPenalty { get; init; }

    // In-game minutes before Opening that a brand-new restaurant starts at (at most;
    // clamped to the start of day). Nullable, code default 30 if unset.
    public int? PreOpeningLeadMinutes { get; init; }
}
