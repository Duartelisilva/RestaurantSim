namespace RestaurantSim.Client.Time;

// Extrapolates the in-game time of day from a snapshot. Ticks are never pushed
// per tick (architecture doc §3), so the client counts forward from the tick the
// snapshot was taken at. Known limit: the server tick loop currently runs at a
// fixed 1 tick/second regardless of TicksPerRealSecond, so this is exact only
// while TicksPerRealSecond == 1.
public sealed class GameClock
{
    private const int MinutesPerDay = 1440;

    private readonly long _snapshotTick;
    private readonly int _minutesPerTick;
    private readonly int _ticksPerRealSecond;
    private readonly DateTimeOffset _receivedAt;

    public GameClock(long snapshotTick, int minutesPerTick, int ticksPerRealSecond, DateTimeOffset receivedAt)
    {
        _snapshotTick = snapshotTick;
        _minutesPerTick = minutesPerTick;
        _ticksPerRealSecond = ticksPerRealSecond;
        _receivedAt = receivedAt;
    }

    public long EstimatedTick(DateTimeOffset now)
    {
        var elapsedSeconds = Math.Max(0, (now - _receivedAt).TotalSeconds);
        return _snapshotTick + (long)Math.Floor(elapsedSeconds * _ticksPerRealSecond);
    }

    public TimeOnly TimeOfDay(DateTimeOffset now)
    {
        var minuteOfDay = (int)(EstimatedTick(now) * _minutesPerTick % MinutesPerDay);
        return new TimeOnly(minuteOfDay / 60, minuteOfDay % 60);
    }
}
