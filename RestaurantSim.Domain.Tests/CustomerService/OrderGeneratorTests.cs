using FluentAssertions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using Xunit;

namespace RestaurantSim.Domain.Tests.CustomerService;

public class OrderGeneratorTests
{
    // Menu ids: drinks 1-3 adult (Water, Soda, Wine), 4 kids (Orange Juice);
    // mains 5 (8.00) and 6 (13.00) adult, 7 kids; desserts 8, 9 adult, 10 kids.
    private static readonly IReadOnlyList<MenuItem> Menu = new[]
    {
        Item(1, ItemPhase.Drinks, MenuItemCategory.Adult, 1.5m),
        Item(2, ItemPhase.Drinks, MenuItemCategory.Adult, 2.0m),
        Item(3, ItemPhase.Drinks, MenuItemCategory.Adult, 3.0m),
        Item(4, ItemPhase.Drinks, MenuItemCategory.Kids, 2.0m),
        Item(5, ItemPhase.EntryMain, MenuItemCategory.Adult, 8.0m),
        Item(6, ItemPhase.EntryMain, MenuItemCategory.Adult, 13.0m),
        Item(7, ItemPhase.EntryMain, MenuItemCategory.Kids, 5.5m),
        Item(8, ItemPhase.Dessert, MenuItemCategory.Adult, 2.0m),
        Item(9, ItemPhase.Dessert, MenuItemCategory.Adult, 3.5m),
        Item(10, ItemPhase.Dessert, MenuItemCategory.Kids, 2.5m)
    };

    private static OrderContext Context(string period = "lunch", IReadOnlyCollection<int>? offered = null) =>
        new(Menu, offered ?? Menu.Select(m => m.MenuItemId).ToList(), period, new ServiceFlowConfig());

    // ---------- Drinks / mains: exactly one each ----------

    [Fact]
    public void Generate_Drinks_EveryMemberGetsExactlyOneDrinkFromTheirOwnMenu_AwayMembersIncluded()
    {
        var members = new[]
        {
            Person(1, adult: true),
            Person(2, adult: false),
            Person(3, adult: true, present: false)
        };

        var choices = OrderGenerator.Generate(ItemPhase.Drinks, members, Context(), new ScriptedRandomProvider(0.0));

        choices.Should().Equal(new OrderChoice(1, 1), new OrderChoice(2, 4), new OrderChoice(3, 1));
    }

    [Fact]
    public void Generate_Mains_ChildNeverGetsAnAdultMain_AndAdultNeverGetsAKidsMain()
    {
        var members = new[] { Person(1, adult: true), Person(2, adult: false) };

        var choices = OrderGenerator.Generate(ItemPhase.EntryMain, members, Context(), new ScriptedRandomProvider(0.99));

        choices.Single(c => c.CustomerId == 1).MenuItemId.Should().BeOneOf(5, 6);
        choices.Single(c => c.CustomerId == 2).MenuItemId.Should().Be(7);
    }

    [Fact]
    public void Generate_ItemNotAvailableInThePeriodOrNotOffered_IsNeverChosen_AndTheMemberIsSkippedWhenNothingFits()
    {
        var members = new[] { Person(1, adult: true) };

        OrderGenerator.Generate(ItemPhase.EntryMain, members, Context(offered: new[] { 6 }), new ScriptedRandomProvider(0.0))
            .Should().Equal(new OrderChoice(1, 6));

        OrderGenerator.Generate(ItemPhase.EntryMain, members, Context(period: "dinner"), new ScriptedRandomProvider(0.0))
            .Should().BeEmpty();
    }

    // ---------- Dessert: optional, per member ----------

    [Fact]
    public void Generate_Dessert_OnlyMembersWhoRollUnderTheirChanceOrder()
    {
        var normal = Person(1, adult: true, SpendingTendency.Normal); // chance 0.35

        // roll 0.2 < 0.35 -> orders; the next number picks the item (0.0 -> first = id 8)
        OrderGenerator.Generate(ItemPhase.Dessert, new[] { normal }, Context(), new ScriptedRandomProvider(0.2, 0.0))
            .Should().Equal(new OrderChoice(1, 8));

        // roll 0.9 >= 0.35 -> no dessert
        OrderGenerator.Generate(ItemPhase.Dessert, new[] { normal }, Context(), new ScriptedRandomProvider(0.9))
            .Should().BeEmpty();
    }

    [Fact]
    public void Generate_Dessert_BudgetRollsLowerButAnyoneCanOrder()
    {
        var budget = Person(1, adult: true, SpendingTendency.Budget); // chance 0.15

        OrderGenerator.Generate(ItemPhase.Dessert, new[] { budget }, Context(), new ScriptedRandomProvider(0.2, 0.0))
            .Should().BeEmpty();
        OrderGenerator.Generate(ItemPhase.Dessert, new[] { budget }, Context(), new ScriptedRandomProvider(0.1, 0.0))
            .Should().Equal(new OrderChoice(1, 8));
    }

    // ---------- Price bias ----------

    [Theory]
    [InlineData(SpendingTendency.Budget, 0.5, 5)]     // cheap main
    [InlineData(SpendingTendency.Budget, 0.9, 6)]     // the priciest main is rare but possible
    [InlineData(SpendingTendency.Normal, 0.4, 5)]     // uniform
    [InlineData(SpendingTendency.Normal, 0.6, 6)]
    [InlineData(SpendingTendency.Generous, 0.2, 5)]
    [InlineData(SpendingTendency.Generous, 0.5, 6)]
    public void Generate_Mains_WeightsByPriceAccordingToSpendingTendency(SpendingTendency tendency, double roll, int expectedItem)
    {
        var choices = OrderGenerator.Generate(
            ItemPhase.EntryMain, new[] { Person(1, true, tendency) }, Context(), new ScriptedRandomProvider(roll));

        choices.Single().MenuItemId.Should().Be(expectedItem);
    }

    [Fact]
    public void Generate_Mains_BudgetPicksThePricierMainRarelyButNotNever_GenerousMoreOften()
    {
        int ExpensivePicks(SpendingTendency tendency)
        {
            var count = 0;
            for (var i = 0; i < 100; i++)
            {
                var roll = i / 100.0;
                var choice = OrderGenerator.Generate(
                    ItemPhase.EntryMain, new[] { Person(1, true, tendency) }, Context(), new ScriptedRandomProvider(roll)).Single();
                if (choice.MenuItemId == 6) count++;
            }

            return count;
        }

        var budget = ExpensivePicks(SpendingTendency.Budget);
        var normal = ExpensivePicks(SpendingTendency.Normal);
        var generous = ExpensivePicks(SpendingTendency.Generous);

        budget.Should().BeGreaterThan(0);
        budget.Should().BeLessThan(normal);
        normal.Should().BeLessThan(generous);
    }

    // ---------- Refills ----------

    [Fact]
    public void GenerateRefills_OnlyMembersWhoRollUnderTheirRefillChanceOrderOneMoreDrink()
    {
        var normal = Person(1, adult: true, SpendingTendency.Normal); // chance 0.5

        OrderGenerator.GenerateRefills(new[] { normal }, Context(), new ScriptedRandomProvider(0.3, 0.0))
            .Should().Equal(new OrderChoice(1, 1));

        OrderGenerator.GenerateRefills(new[] { normal }, Context(), new ScriptedRandomProvider(0.7))
            .Should().BeEmpty();
    }

    [Fact]
    public void GenerateRefills_ChildRefillIsAKidsDrink()
    {
        var kid = Person(1, adult: false, SpendingTendency.Generous); // chance 0.7

        OrderGenerator.GenerateRefills(new[] { kid }, Context(), new ScriptedRandomProvider(0.1, 0.0))
            .Should().Equal(new OrderChoice(1, 4));
    }

    // ---------- helpers ----------

    private static Customer Person(
        int id, bool adult, SpendingTendency tendency = SpendingTendency.Normal, bool present = true) => new()
    {
        CustomerId = id,
        IsAdult = adult,
        PatienceLevel = 0.5f,
        Instability = 0.5f,
        SusceptibilityToGroup = 0.5f,
        SpendingTendency = tendency,
        IsRegular = false,
        Gender = Gender.Male,
        IsPresentAtTable = present
    };

    private static MenuItem Item(int id, ItemPhase course, MenuItemCategory category, decimal price) => new()
    {
        MenuItemId = id,
        Name = "Item " + id,
        Category = category,
        Course = course,
        PrepTimeSeconds = 5,
        DefaultPrice = price,
        AvailableMealPeriods = new[] { "lunch" }
    };
}
