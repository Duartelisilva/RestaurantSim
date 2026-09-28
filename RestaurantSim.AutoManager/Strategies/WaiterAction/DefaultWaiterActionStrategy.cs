using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;

namespace RestaurantSim.AutoManager.Strategies.WaiterAction;

// mechanics doc §11: per occupied cluster, checks cooldown, rolls a notice-
// and-joke chance based on the table's lowest-Mood present member's tier,
// then decides person- vs table-targeting via MoodGapThreshold. Stops at the
// FIRST cluster that qualifies this pass — deliberately one joke per
// AutoManager pass, not draining every eligible table at once.
public sealed class DefaultWaiterActionStrategy : IWaiterActionStrategy
{
    private readonly IRandomProvider _random;

    public DefaultWaiterActionStrategy(IRandomProvider random)
    {
        _random = random;
    }

    public (int ClusterId, int? TargetCustomerId)? DecideNextJoke(Restaurant restaurant, float autoJokeCooldownTicks, float moodGapThreshold)
    {
        foreach (var cluster in restaurant.Floor.Clusters.Where(c => c.OccupiedBy is not null))
        {
            if (restaurant.SimulationTick - cluster.LastJokeTick < autoJokeCooldownTicks) continue;

            var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == cluster.OccupiedBy);
            if (group is null) continue;

            var presentMembers = group.Members.Where(m => m.IsPresentAtTable).ToList();
            if (presentMembers.Count == 0) continue;

            var lowestMoodMember = presentMembers.OrderBy(m => m.Mood).First();
            var triggerChance = GetTriggerChance(MoodTierCalculator.GetTier(lowestMoodMember.Mood));

            if (_random.NextDouble() >= triggerChance) continue;

            int? targetId = null;
            if (presentMembers.Count > 1)
            {
                foreach (var member in presentMembers)
                {
                    var othersAvg = presentMembers.Where(m => m.CustomerId != member.CustomerId).Average(m => m.Mood);
                    if (othersAvg - member.Mood >= moodGapThreshold)
                    {
                        targetId = member.CustomerId;
                        break;
                    }
                }
            }

            return (cluster.ClusterId, targetId);
        }

        return null;
    }

    private static float GetTriggerChance(MoodTier tier) => tier switch
    {
        MoodTier.VeryLow => 0.20f,
        MoodTier.Low => 0.12f,
        MoodTier.Mid => 0.04f,
        _ => 0.01f
    };
}
