namespace RestaurantSim.Domain.Staff;

// "A real restaurant gives the new table to whoever has the fewest clients" — used when
// tables are merged, so work stays evenly distributed.
public static class WaiterWorkload
{
    // The on-shift waiter currently serving the fewest seated groups; ties go to the
    // lowest StaffId. Null when no waiter is on shift.
    public static int? LeastLoadedWaiterId(Restaurant restaurant)
    {
        var waiters = restaurant.StaffRoster.Staff
            .Where(s => s.Role == StaffRole.Waiter && s.State != StaffState.OffShift)
            .ToList();

        if (waiters.Count == 0)
            return null;

        return waiters
            .OrderBy(w => restaurant.Floor.Clusters.Count(c => c.OccupiedBy is not null && c.AssignedWaiterId == w.StaffId))
            .ThenBy(w => w.StaffId)
            .First()
            .StaffId;
    }
}
