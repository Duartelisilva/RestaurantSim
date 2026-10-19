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
}
