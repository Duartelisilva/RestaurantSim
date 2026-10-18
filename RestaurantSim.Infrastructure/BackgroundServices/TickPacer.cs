namespace RestaurantSim.Infrastructure.BackgroundServices;

// Decides when each restaurant is due its next tick, from that restaurant's own
// TicksPerRealSecond (architecture doc §2, mechanics doc §14) — so every
// duration authored in real seconds stays that many real seconds whatever the
// rate is. Pure scheduling logic (time is passed in), single-threaded: only the
// hosting loop calls it.
public sealed class TickPacer
{
    private readonly Dictionary<Guid, TimeSpan> _nextDue = new();

    public bool ShouldTick(Guid restaurantId, TimeSpan now, int ticksPerRealSecond)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, ticksPerRealSecond));

        // First time this restaurant is seen: its first tick is one interval away.
        if (!_nextDue.TryGetValue(restaurantId, out var due))
        {
            _nextDue[restaurantId] = now + interval;
            return false;
        }

        if (now < due)
            return false;

        // Keep the average rate when slightly late; if more than a full interval
        // behind (an overloaded host), drop the backlog and slow down instead of
        // bursting ticks to catch up.
        var next = due + interval;
        _nextDue[restaurantId] = next > now ? next : now + interval;
        return true;
    }
}
