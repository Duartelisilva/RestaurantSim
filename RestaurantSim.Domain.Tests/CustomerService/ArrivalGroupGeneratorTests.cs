using FluentAssertions;
using RestaurantSim.Domain.CustomerService;
using Xunit;

namespace RestaurantSim.Domain.Tests.CustomerService;

public class ArrivalGroupGeneratorTests
{
    [Fact]
    public void Generate_FirstMemberIsAlwaysAdult()
    {
        var random = new ScriptedRandomProvider(0.01);
        var generator = new ArrivalGroupGenerator(random);

        var group = generator.Generate(groupId: 1, arrivalTick: 0, NextId());

        group.Members[0].IsAdult.Should().BeTrue();
        group.PayerId.Should().Be(group.Members[0].CustomerId);
    }

    [Fact]
    public void Generate_PartySizeIsWithinValidRange()
    {
        var random = new ScriptedRandomProvider(0.99, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5);
        var generator = new ArrivalGroupGenerator(random);

        var group = generator.Generate(groupId: 1, arrivalTick: 0, NextId());

        group.PartySize.Should().BeInRange(1, 6);
        group.Members.Should().HaveCount(group.PartySize);
    }

    [Fact]
    public void Generate_MoodIsClampedWithinZeroToHundred()
    {
        var random = new ScriptedRandomProvider(0.5, 0.99, 0.99, 0.99, 0.99, 0.99, 0.99, 0.99, 0.99);
        var generator = new ArrivalGroupGenerator(random);

        var group = generator.Generate(groupId: 1, arrivalTick: 0, NextId());

        // Plain comparisons — FluentAssertions builds an expression tree here, and
        // pattern-matching ("is >= 0 and <= 1") isn't allowed inside one (CS8122).
        group.Members.Should().OnlyContain(m => m.Mood >= 0 && m.Mood <= 100);
    }

    [Fact]
    public void Generate_TraitsAreClampedWithinZeroToOne()
    {
        var random = new ScriptedRandomProvider(0.5, 0.99, 0.01, 0.99, 0.01, 0.99, 0.01);
        var generator = new ArrivalGroupGenerator(random);

        var group = generator.Generate(groupId: 1, arrivalTick: 0, NextId());

        group.Members.Should().OnlyContain(m =>
            m.PatienceLevel >= 0 && m.PatienceLevel <= 1 &&
            m.Instability >= 0 && m.Instability <= 1 &&
            m.SusceptibilityToGroup >= 0 && m.SusceptibilityToGroup <= 1);
    }

    [Fact]
    public void Generate_SetsLifecycleStateToQueued()
    {
        var random = new ScriptedRandomProvider(0.5);
        var generator = new ArrivalGroupGenerator(random);

        var group = generator.Generate(groupId: 1, arrivalTick: 42, NextId());

        group.LifecycleState.Should().Be(GroupLifecycleState.Queued);
        group.ArrivalTick.Should().Be(42);
    }

    [Fact]
    public void Generate_AssignsUniqueSequentialCustomerIds()
    {
        var random = new ScriptedRandomProvider(0.99, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5);
        var generator = new ArrivalGroupGenerator(random);

        var group = generator.Generate(groupId: 1, arrivalTick: 0, NextId());

        group.Members.Select(m => m.CustomerId).Should().OnlyHaveUniqueItems();
    }

    // Simple monotonic counter closure, standing in for
    // Restaurant.CustomerService.NextCustomerId++ in real use.
    private static Func<int> NextId()
    {
        var counter = 0;
        return () => ++counter;
    }
}
