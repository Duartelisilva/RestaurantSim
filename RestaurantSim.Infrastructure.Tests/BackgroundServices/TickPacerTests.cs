using FluentAssertions;
using RestaurantSim.Infrastructure.BackgroundServices;

namespace RestaurantSim.Infrastructure.Tests.BackgroundServices;

public class TickPacerTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private readonly TickPacer _pacer = new();

    // Simulates the hosting loop: wakes every 10 ms for the given duration.
    private int CountTicks(Guid id, int ticksPerRealSecond, int fromMs, int toMs)
    {
        var ticks = 0;
        for (var ms = fromMs; ms <= toMs; ms += 10)
        {
            if (_pacer.ShouldTick(id, TimeSpan.FromMilliseconds(ms), ticksPerRealSecond))
                ticks++;
        }

        return ticks;
    }

    [Fact]
    public void FirstSighting_NeverTicks()
    {
        _pacer.ShouldTick(A, TimeSpan.Zero, 1).Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(10, 10)]
    public void OneRealSecond_ProducesExactlyTicksPerRealSecondTicks(int ticksPerRealSecond, int expectedTicks)
    {
        CountTicks(A, ticksPerRealSecond, 0, 1000).Should().Be(expectedTicks);
    }

    [Fact]
    public void DoesNotTickTwiceWithinOneInterval()
    {
        _pacer.ShouldTick(A, TimeSpan.Zero, 1);

        _pacer.ShouldTick(A, TimeSpan.FromMilliseconds(1000), 1).Should().BeTrue();
        _pacer.ShouldTick(A, TimeSpan.FromMilliseconds(1010), 1).Should().BeFalse();
    }

    [Fact]
    public void Restaurants_AreTimedIndependently()
    {
        CountTicks(A, 1, 0, 2000).Should().Be(2);
        CountTicks(B, 4, 0, 2000).Should().Be(8);
    }

    [Fact]
    public void LongStall_DropsTheBacklogInsteadOfBurstingTicks()
    {
        _pacer.ShouldTick(A, TimeSpan.Zero, 1);

        // The host froze for 5 s: exactly one tick, then a normal full interval.
        _pacer.ShouldTick(A, TimeSpan.FromSeconds(5), 1).Should().BeTrue();
        _pacer.ShouldTick(A, TimeSpan.FromMilliseconds(5010), 1).Should().BeFalse();
        _pacer.ShouldTick(A, TimeSpan.FromMilliseconds(5990), 1).Should().BeFalse();
        _pacer.ShouldTick(A, TimeSpan.FromMilliseconds(6000), 1).Should().BeTrue();
    }

    [Fact]
    public void RateZeroOrNegative_IsTreatedAsOne()
    {
        CountTicks(A, 0, 0, 1000).Should().Be(1);
    }
}
