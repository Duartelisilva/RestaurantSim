using FluentAssertions;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Domain.Tests.Scheduling;

public class TickRatesTests
{
    [Theory]
    [InlineData(-0.4f, 1, -0.4f)]
    [InlineData(-0.4f, 2, -0.2f)]
    [InlineData(0.5f, 4, 0.125f)]
    public void PerTick_DividesTheRateByTicksPerRealSecond(float perSecond, int ticksPerRealSecond, float expected)
    {
        TickRates.PerTick(perSecond, ticksPerRealSecond).Should().BeApproximately(expected, 0.00001f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void OneRealSecondOfTicks_AlwaysAppliesTheFullPerSecondRate(int ticksPerRealSecond)
    {
        var perTick = TickRates.PerTick(0.5f, ticksPerRealSecond);

        (perTick * ticksPerRealSecond).Should().BeApproximately(0.5f, 0.00001f);
    }

    [Fact]
    public void PerTick_ZeroOrNegativeTickRate_IsTreatedAsOne()
    {
        TickRates.PerTick(0.5f, 0).Should().Be(0.5f);
    }

    [Theory]
    [InlineData(10f, 1, 10)]
    [InlineData(10f, 2, 20)]   // a 10 s duration stays 10 real seconds at 2 ticks per second
    [InlineData(10f, 4, 40)]
    [InlineData(2.5f, 1, 3)]   // round half up
    [InlineData(0.2f, 1, 1)]   // never 0 ticks
    [InlineData(0f, 1, 1)]
    public void SecondsToTicks_ConvertsRealSecondsToTicks(float seconds, int ticksPerRealSecond, long expected)
    {
        TickRates.SecondsToTicks(seconds, ticksPerRealSecond).Should().Be(expected);
    }
}
