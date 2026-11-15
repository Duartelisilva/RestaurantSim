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

        var load = SeatedGroupsByWaiter(restaurant);

        return waiters
            .OrderBy(w => load.GetValueOrDefault(w.StaffId))
            .ThenBy(w => w.StaffId)
            .First()
            .StaffId;
    }

    // Seated groups per waiter: the occupied clusters assigned to each. A waiter with nobody
    // seated is simply absent (read it with GetValueOrDefault).
    public static IReadOnlyDictionary<int, int> SeatedGroupsByWaiter(Restaurant restaurant)
    {
        return restaurant.Floor.Clusters
            .Where(c => c.OccupiedBy is not null)
            .Select(c => c.AssignedWaiterId)
            .OfType<int>()
            .GroupBy(waiterId => waiterId)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
