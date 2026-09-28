namespace RestaurantSim.Domain.CustomerService;

public enum MoodTier
{
    VeryLow,
    Low,
    Mid,
    High
}

// Mechanics doc §2's locked tier boundaries — extracted as a shared utility
// since both TellJokeCommand and DefaultWaiterActionStrategy need it.
public static class MoodTierCalculator
{
    public static MoodTier GetTier(float mood) => mood switch
    {
        >= 70 => MoodTier.High,
        >= 40 => MoodTier.Mid,
        >= 15 => MoodTier.Low,
        _ => MoodTier.VeryLow
    };
}
