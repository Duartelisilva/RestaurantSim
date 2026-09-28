using RestaurantSim.Domain;

namespace RestaurantSim.AutoManager.Strategies.WaiterAction;

public interface IWaiterActionStrategy
{
    (int ClusterId, int? TargetCustomerId)? DecideNextJoke(Restaurant restaurant, float autoJokeCooldownTicks, float moodGapThreshold);
}
