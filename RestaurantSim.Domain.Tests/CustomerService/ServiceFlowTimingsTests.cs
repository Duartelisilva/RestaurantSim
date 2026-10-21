using FluentAssertions;
using RestaurantSim.Domain.CustomerService;
using Xunit;

namespace RestaurantSim.Domain.Tests.CustomerService;

public class ServiceFlowTimingsTests
{
    [Theory]
    [InlineData(0.7f, 1, 0.7f)]
    [InlineData(0.7f, 2, 0.85f)]
    [InlineData(0.7f, 3, 0.925f)]
    [InlineData(1.0f, 4, 1.0f)]
    [InlineData(0.7f, 0, 0.7f)]
    public void ReadyToOrderChance_EachRecheckHalvesTheNotReadyOdds(float baseChance, int attempt, float expected)
    {
        ServiceFlowTimings.ReadyToOrderChance(baseChance, attempt).Should().BeApproximately(expected, 0.0001f);
    }

    [Theory]
    [InlineData(0.0, 1, 18)]
    [InlineData(0.5, 1, 26)]
    [InlineData(1.0, 1, 34)]
    [InlineData(0.5, 2, 52)]   // same 26 real seconds at 2 ticks per second
    public void RollEatingTicks_IsUniformInTheRangeAndScalesWithTheTickRate(double roll, int ticksPerRealSecond, long expected)
    {
        var random = new ScriptedRandomProvider(roll);

        ServiceFlowTimings.RollEatingTicks(new DurationRange(18f, 34f), ticksPerRealSecond, random).Should().Be(expected);
    }

    [Fact]
    public void RollEatingTicks_IsNeverZero()
    {
        var random = new ScriptedRandomProvider(0.0);

        ServiceFlowTimings.RollEatingTicks(new DurationRange(0f, 0.1f), 1, random).Should().Be(1);
    }
}
