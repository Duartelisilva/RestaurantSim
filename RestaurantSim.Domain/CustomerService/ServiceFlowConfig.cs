using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Domain.CustomerService;

public sealed record DurationRange(float MinSeconds, float MaxSeconds);

// Timings, chances and eating durations that drive a group through ordering,
// eating, paying and leaving (mechanics doc §17, draft). Durations are authored in
// real-world seconds and converted with TickRates.SecondsToTicks (mechanics doc
// §14), so they stay that many real seconds at any TicksPerRealSecond. The
// defaults below are the owner-approved first-pass values.
public sealed record ServiceFlowConfig
{
    public float SeatedToFirstVisitSeconds { get; init; } = 3f;
    public float RecheckDelaySeconds { get; init; } = 4f;
    public float DrinksToMainDelaySeconds { get; init; } = 5f;
    public float MainToRefillVisitDelaySeconds { get; init; } = 4f;
    public float MainToDessertDelaySeconds { get; init; } = 6f;
    public float FinalCourseToBillDelaySeconds { get; init; } = 4f;
    public float PaymentToLeaveSeconds { get; init; } = 2f;

    // Chance the group is ready to order at its FIRST visit for each phase; each
    // recheck halves the not-ready odds (ServiceFlowTimings.ReadyToOrderChance).
    public IReadOnlyDictionary<ItemPhase, float> ReadyToOrderChance { get; init; } = new Dictionary<ItemPhase, float>
    {
        [ItemPhase.Drinks] = 0.9f,
        [ItemPhase.EntryMain] = 0.7f,
        [ItemPhase.Dessert] = 0.8f
    };

    // Per member: chance of one extra drink at the mid-meal refill visit.
    public IReadOnlyDictionary<SpendingTendency, float> DrinkRefillChance { get; init; } = new Dictionary<SpendingTendency, float>
    {
        [SpendingTendency.Budget] = 0.3f,
        [SpendingTendency.Normal] = 0.5f,
        [SpendingTendency.Generous] = 0.7f
    };

    // Per member: chance of ordering a dessert (anyone may, Budget just rolls lower).
    public IReadOnlyDictionary<SpendingTendency, float> DessertOrderChance { get; init; } = new Dictionary<SpendingTendency, float>
    {
        [SpendingTendency.Budget] = 0.15f,
        [SpendingTendency.Normal] = 0.35f,
        [SpendingTendency.Generous] = 0.6f
    };

    // Item weight = DefaultPrice ^ bias. Strongly negative: rarely picks the priciest
    // item but still can; positive leans pricier; 0 is uniform.
    public IReadOnlyDictionary<SpendingTendency, float> SpendingPriceBias { get; init; } = new Dictionary<SpendingTendency, float>
    {
        [SpendingTendency.Budget] = -3f,
        [SpendingTendency.Normal] = 0f,
        [SpendingTendency.Generous] = 1.5f
    };

    public IReadOnlyDictionary<ItemPhase, DurationRange> EatingDurationSeconds { get; init; } = new Dictionary<ItemPhase, DurationRange>
    {
        [ItemPhase.Drinks] = new DurationRange(8f, 16f),
        [ItemPhase.EntryMain] = new DurationRange(18f, 34f),
        [ItemPhase.Dessert] = new DurationRange(8f, 16f)
    };

    // Lookups with conservative fallbacks, so a preset that omits a key can never
    // stall the flow (ready = always) or invent extra orders (chances = never).
    public float GetReadyToOrderChance(ItemPhase phase) =>
        ReadyToOrderChance.TryGetValue(phase, out var chance) ? chance : 1f;

    public float GetDrinkRefillChance(SpendingTendency tendency) =>
        DrinkRefillChance.TryGetValue(tendency, out var chance) ? chance : 0f;

    public float GetDessertChance(SpendingTendency tendency) =>
        DessertOrderChance.TryGetValue(tendency, out var chance) ? chance : 0f;

    public float GetPriceBias(SpendingTendency tendency) =>
        SpendingPriceBias.TryGetValue(tendency, out var bias) ? bias : 0f;

    public DurationRange GetEatingDuration(ItemPhase phase) =>
        EatingDurationSeconds.TryGetValue(phase, out var range) ? range : new DurationRange(1f, 1f);
}
