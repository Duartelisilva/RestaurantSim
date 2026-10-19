using FluentAssertions;
using RestaurantSim.Client.Time;

namespace RestaurantSim.Client.Tests.Time;

public class GameClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(720, 1, 12, 0)]
    [InlineData(72, 10, 12, 0)]
    [InlineData(1439, 1, 23, 59)]
    public void TimeOfDay_AtSnapshotMoment_MatchesTick(long tick, int minutesPerTick, int hour, int minute)
    {
        var clock = new GameClock(tick, minutesPerTick, 1, T0);

        clock.TimeOfDay(T0).Should().Be(new TimeOnly(hour, minute));
    }

    [Fact]
    public void TimeOfDay_AdvancesOneGameMinutePerRealSecond()
    {
        var clock = new GameClock(0, 1, 1, T0);

        clock.TimeOfDay(T0.AddSeconds(30)).Should().Be(new TimeOnly(0, 30));
    }

    [Fact]
    public void TimeOfDay_WrapsPastMidnight()
    {
        var clock = new GameClock(1439, 1, 1, T0);

        clock.TimeOfDay(T0.AddSeconds(2)).Should().Be(new TimeOnly(0, 1));
    }

    [Fact]
    public void TimeOfDay_AtAHigherTickRate_KeepsTheSameGamePace()
    {
        // 4 ticks per real second at 0.25 game minutes per tick = 1 game minute per second.
        var clock = new GameClock(0, 0.25, 4, T0);

        clock.TimeOfDay(T0.AddSeconds(240)).Should().Be(new TimeOnly(4, 0));
    }

    [Fact]
    public void EstimatedTick_NeverGoesBackwards_WhenNowIsBeforeReceipt()
    {
        var clock = new GameClock(100, 1, 1, T0);

        clock.EstimatedTick(T0.AddSeconds(-5)).Should().Be(100);
    }

    [Fact]
    public void EstimatedTick_ScalesWithTicksPerRealSecond()
    {
        var clock = new GameClock(0, 1, 4, T0);

        clock.EstimatedTick(T0.AddSeconds(3)).Should().Be(12);
    }
}
