using RestaurantSim.Domain;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.AutoManager.Strategies.TaskPriority;

// Only consulted for IDLE Bussers choosing their NEXT task — "finish current
// task before switching" (tier 1 of mechanics doc §13's ordering) is handled
// by the caller simply never asking a Busy Busser for a new decision, not by
// this strategy itself.
public interface IStaffTaskPriorityStrategy
{
    // dirtyThreshold/almostDirtyThreshold passed in rather than resolved
    // internally — keeps the strategy pure decision logic, config resolution
    // (including fallback defaults) stays the orchestrator's job.
    (TaskType TaskType, int TargetId)? DecideNextBusserTask(Restaurant restaurant, float dirtyThreshold, float almostDirtyThreshold);
}
