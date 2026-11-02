using FluentAssertions;
using RestaurantSim.Client.Components;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.Components;

public class DiningLabelsTests
{
    [Theory]
    [InlineData(DiningVisitNames.Drinks, "choosing drinks")]
    [InlineData(DiningVisitNames.Main, "choosing the main course")]
    [InlineData(DiningVisitNames.Refill, "deciding on a refill")]
    [InlineData(DiningVisitNames.Dessert, "choosing dessert")]
    public void Describe_Ordering_NamesWhatTheGroupIsChoosing(string nextVisit, string expected)
    {
        DiningLabels.Describe(DiningStageNames.Ordering, nextVisit).Should().Be(expected);
    }

    [Theory]
    [InlineData(DiningStageNames.WaitingForFood, "waiting for food")]
    [InlineData(DiningStageNames.Eating, "eating")]
    [InlineData(DiningStageNames.Paying, "paying")]
    [InlineData(DiningStageNames.Leaving, "leaving")]
    public void Describe_OtherStages_IgnoreTheNextVisit(string stage, string expected)
    {
        DiningLabels.Describe(stage, DiningVisitNames.Main).Should().Be(expected);
    }

    [Fact]
    public void Describe_UnknownStage_FallsBackToTheRawName()
    {
        DiningLabels.Describe("Brunching", DiningVisitNames.Main).Should().Be("Brunching");
    }

    [Theory]
    [InlineData(DiningStageNames.Ordering, "stage-ordering")]
    [InlineData(DiningStageNames.WaitingForFood, "stage-waitingforfood")]
    [InlineData(DiningStageNames.Eating, "stage-eating")]
    [InlineData(DiningStageNames.Paying, "stage-paying")]
    [InlineData(DiningStageNames.Leaving, "stage-leaving")]
    public void CssClass_IsTheLowercasedStageNameWithAPrefix(string stage, string expected)
    {
        DiningLabels.CssClass(stage).Should().Be(expected);
    }
}
