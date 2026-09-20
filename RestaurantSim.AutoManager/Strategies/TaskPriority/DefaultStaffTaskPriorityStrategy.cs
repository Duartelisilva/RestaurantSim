using RestaurantSim.Domain;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.AutoManager.Strategies.TaskPriority;

// Mechanics doc §13's revised tiered ordering (tier 1, "finish current task,"
// is the caller's job, not this strategy's — see interface comment):
//   2. Urgent bathroom (CleanlinessScore <= DirtyThreshold, under
//      MaxConcurrentCleaners) — outranks tables
//   3. Any dirty table
//   4. Cleaning-eligible bathroom (<= AlmostDirtyThreshold, under
//      MaxConcurrentCleaners, not already urgent)
//   5. Idle
public sealed class DefaultStaffTaskPriorityStrategy : IStaffTaskPriorityStrategy
{
    public (TaskType TaskType, int TargetId)? DecideNextBusserTask(Restaurant restaurant, float dirtyThreshold, float almostDirtyThreshold)
    {
        var urgentBathroom = restaurant.Floor.Bathrooms
            .Where(b => b.CleanlinessScore <= dirtyThreshold)
            .Where(b => AssignedBusserCount(restaurant, b.BathroomId) < b.MaxConcurrentCleaners)
            .OrderBy(b => b.CleanlinessScore) // dirtiest first among urgent ones
            .FirstOrDefault();
        if (urgentBathroom is not null)
            return (TaskType.CleanBathroom, urgentBathroom.BathroomId);

        var dirtyTable = restaurant.Floor.Tables.FirstOrDefault(t => !t.IsClean);
        if (dirtyTable is not null)
            return (TaskType.CleanTable, dirtyTable.TableId);

        var eligibleBathroom = restaurant.Floor.Bathrooms
            .Where(b => b.CleanlinessScore <= almostDirtyThreshold && b.CleanlinessScore > dirtyThreshold)
            .Where(b => AssignedBusserCount(restaurant, b.BathroomId) < b.MaxConcurrentCleaners)
            .OrderBy(b => b.CleanlinessScore)
            .FirstOrDefault();
        if (eligibleBathroom is not null)
            return (TaskType.CleanBathroom, eligibleBathroom.BathroomId);

        return null;
    }

    private static int AssignedBusserCount(Restaurant restaurant, int bathroomId) =>
        restaurant.StaffRoster.Staff.Count(s =>
            s.Role == StaffRole.Busser && s.CurrentTaskType == TaskType.CleanBathroom && s.CurrentTaskTargetId == bathroomId);
}
