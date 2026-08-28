namespace RestaurantSim.Domain.Staff;

public enum StatType
{
    Speed,
    Fatigue,
    ErrorRate,
    Charisma,
    Experience,
    Morale
}

// Open stat-bag rather than fixed fields — additive when new stats are needed
// later (DLC training system, Cook-only precision stat) with no schema change.
public sealed class StaffStats
{
    private readonly Dictionary<StatType, float> _values = new();

    // 0.5 = "average" (no modifier vs authored base values) per entity doc §7.
    public float Get(StatType type) => _values.TryGetValue(type, out var value) ? value : 0.5f;

    public void Set(StatType type, float value) => _values[type] = value;
}
