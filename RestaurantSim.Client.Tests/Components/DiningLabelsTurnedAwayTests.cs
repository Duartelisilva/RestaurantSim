using FluentAssertions;
using RestaurantSim.Client.Components;
using RestaurantSim.Contracts.Events;

namespace RestaurantSim.Client.Tests.Components;

public class DiningLabelsTurnedAwayTests
{
    [Theory]
    [InlineData(TurnedAwayReasonNames.QueueFull, "queue full")]
    [InlineData(TurnedAwayReasonNames.PartyTooLarge, "party too large for any table")]
    [InlineData("Whatever", "Whatever")]
    public void TurnedAway_WordsTheReason(string reason, string expected)
    {
        DiningLabels.TurnedAway(reason).Should().Be(expected);
    }

    [Fact]
    public void TableList_IsOrderedAndJoinedWithPlus()
    {
        DiningLabels.TableList([4, 3]).Should().Be("T3 + T4");
        DiningLabels.TableList([6]).Should().Be("T6");
    }
}
