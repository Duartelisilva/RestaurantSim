using FluentAssertions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using Xunit;

namespace RestaurantSim.Domain.Tests.CustomerService;

public class ServiceFlowConfigTests
{
    // Locks the owner-approved first-pass values so an accidental edit is caught.
    [Fact]
    public void Defaults_MatchTheApprovedValues()
    {
        var flow = new ServiceFlowConfig();

        flow.SeatedToFirstVisitSeconds.Should().Be(3f);
        flow.RecheckDelaySeconds.Should().Be(4f);
        flow.DrinksToMainDelaySeconds.Should().Be(5f);
        flow.MainToRefillVisitDelaySeconds.Should().Be(4f);
        flow.MainToDessertDelaySeconds.Should().Be(6f);
        flow.FinalCourseToBillDelaySeconds.Should().Be(4f);
        flow.PaymentToLeaveSeconds.Should().Be(2f);

        flow.GetReadyToOrderChance(ItemPhase.Drinks).Should().Be(0.9f);
        flow.GetReadyToOrderChance(ItemPhase.EntryMain).Should().Be(0.7f);
        flow.GetReadyToOrderChance(ItemPhase.Dessert).Should().Be(0.8f);

        flow.GetDrinkRefillChance(SpendingTendency.Budget).Should().Be(0.3f);
        flow.GetDrinkRefillChance(SpendingTendency.Normal).Should().Be(0.5f);
        flow.GetDrinkRefillChance(SpendingTendency.Generous).Should().Be(0.7f);

        flow.GetDessertChance(SpendingTendency.Budget).Should().Be(0.15f);
        flow.GetDessertChance(SpendingTendency.Normal).Should().Be(0.35f);
        flow.GetDessertChance(SpendingTendency.Generous).Should().Be(0.6f);

        flow.GetPriceBias(SpendingTendency.Budget).Should().Be(-3f);
        flow.GetPriceBias(SpendingTendency.Normal).Should().Be(0f);
        flow.GetPriceBias(SpendingTendency.Generous).Should().Be(1.5f);

        flow.GetEatingDuration(ItemPhase.Drinks).Should().Be(new DurationRange(8f, 16f));
        flow.GetEatingDuration(ItemPhase.EntryMain).Should().Be(new DurationRange(18f, 34f));
        flow.GetEatingDuration(ItemPhase.Dessert).Should().Be(new DurationRange(8f, 16f));
    }

    [Fact]
    public void MissingKeys_FallBackSafely_NeverStallingTheFlowOrInventingOrders()
    {
        var flow = new ServiceFlowConfig
        {
            ReadyToOrderChance = new Dictionary<ItemPhase, float>(),
            DrinkRefillChance = new Dictionary<SpendingTendency, float>(),
            DessertOrderChance = new Dictionary<SpendingTendency, float>(),
            SpendingPriceBias = new Dictionary<SpendingTendency, float>(),
            EatingDurationSeconds = new Dictionary<ItemPhase, DurationRange>()
        };

        flow.GetReadyToOrderChance(ItemPhase.Drinks).Should().Be(1f);
        flow.GetDrinkRefillChance(SpendingTendency.Normal).Should().Be(0f);
        flow.GetDessertChance(SpendingTendency.Normal).Should().Be(0f);
        flow.GetPriceBias(SpendingTendency.Normal).Should().Be(0f);
        flow.GetEatingDuration(ItemPhase.Drinks).Should().Be(new DurationRange(1f, 1f));
    }
}
