namespace RestaurantSim.Domain.Staff;

// Shared Speed/Fatigue curve (mechanics doc §10) — mechanics doc §13
// explicitly reuses this same shape for Bussers: "same mechanism, different
// stat owner." Extracted here so KitchenPrepTimeCalculator and Busser
// cleaning-progress calculations both call one implementation, not two
// copies of the same formula.
public static class PerformanceMultipliers
{
    public static float SpeedMultiplier(float speed) => 1.6f - 1.2f * speed;

    public static float FatigueMultiplier(float fatigue) => 1.0f + 0.5f * fatigue;
}
