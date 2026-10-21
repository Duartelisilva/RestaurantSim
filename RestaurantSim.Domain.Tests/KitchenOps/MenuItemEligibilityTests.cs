using FluentAssertions;
using RestaurantSim.Domain.KitchenOps;
using Xunit;

namespace RestaurantSim.Domain.Tests.KitchenOps;

public class MenuItemEligibilityTests
{
    [Fact]
    public void IsOrderable_MatchingCourseAdultAndPeriod_IsTrue()
    {
        var item = Item(ItemPhase.EntryMain, MenuItemCategory.Adult, "lunch");

        MenuItemEligibility.IsOrderable(item, ItemPhase.EntryMain, customerIsAdult: true, "lunch").Should().BeTrue();
    }

    [Fact]
    public void IsOrderable_WrongCourse_IsFalse()
    {
        var item = Item(ItemPhase.Dessert, MenuItemCategory.Adult, "lunch");

        MenuItemEligibility.IsOrderable(item, ItemPhase.EntryMain, true, "lunch").Should().BeFalse();
    }

    [Fact]
    public void IsOrderable_ChildCannotOrderAdultItem()
    {
        var item = Item(ItemPhase.EntryMain, MenuItemCategory.Adult, "lunch");

        MenuItemEligibility.IsOrderable(item, ItemPhase.EntryMain, customerIsAdult: false, "lunch").Should().BeFalse();
    }

    [Fact]
    public void IsOrderable_ChildCanOrderKidsItem()
    {
        var item = Item(ItemPhase.EntryMain, MenuItemCategory.Kids, "lunch");

        MenuItemEligibility.IsOrderable(item, ItemPhase.EntryMain, false, "lunch").Should().BeTrue();
    }

    [Fact]
    public void IsOrderable_AdultMayOrderKidsItem_TheRuleAloneDoesNotForbidIt()
    {
        var item = Item(ItemPhase.EntryMain, MenuItemCategory.Kids, "lunch");

        MenuItemEligibility.IsOrderable(item, ItemPhase.EntryMain, true, "lunch").Should().BeTrue();
    }

    [Fact]
    public void IsOrderable_ItemNotAvailableInThePeriod_IsFalse()
    {
        var item = Item(ItemPhase.EntryMain, MenuItemCategory.Adult, "dinner");

        MenuItemEligibility.IsOrderable(item, ItemPhase.EntryMain, true, "lunch").Should().BeFalse();
    }

    private static MenuItem Item(ItemPhase course, MenuItemCategory category, string period) => new()
    {
        MenuItemId = 1,
        Name = "Test",
        Category = category,
        Course = course,
        PrepTimeSeconds = 5,
        DefaultPrice = 10m,
        AvailableMealPeriods = new[] { period }
    };
}
