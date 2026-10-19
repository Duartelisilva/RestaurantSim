using FluentAssertions;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Domain.Tests.Scheduling;

public class MealPeriodCalculatorTests
{
    [Fact]
    public void InitialTick_StartsLeadMinutesBeforeOpening()
    {
        var periods = new[] { Period("lunch", 12, 0, 15, 0) };

        MealPeriodCalculator.InitialTick(periods, minutesPerTick: 1, leadMinutes: 30).Should().Be(690);
    }

    [Fact]
    public void InitialTick_ZeroLead_StartsExactlyAtOpening()
    {
        var periods = new[] { Period("lunch", 12, 0, 15, 0) };

        MealPeriodCalculator.InitialTick(periods, 1, 0).Should().Be(720);
    }

    [Fact]
    public void InitialTick_NegativeLead_IsTreatedAsZero()
    {
        var periods = new[] { Period("lunch", 12, 0, 15, 0) };

        MealPeriodCalculator.InitialTick(periods, 1, -15).Should().Be(720);
    }

    [Fact]
    public void InitialTick_LeadLargerThanOpening_ClampsToStartOfDay()
    {
        var periods = new[] { Period("breakfast", 0, 10, 3, 0) };

        MealPeriodCalculator.InitialTick(periods, 1, 30).Should().Be(0);
    }

    [Fact]
    public void InitialTick_UsesTheEarliestPeriod()
    {
        var periods = new[] { Period("dinner", 19, 0, 0, 0), Period("lunch", 12, 0, 15, 0) };

        MealPeriodCalculator.InitialTick(periods, 1, 30).Should().Be(690);
    }

    [Fact]
    public void InitialTick_ConvertsMinutesToTicks()
    {
        var periods = new[] { Period("lunch", 12, 0, 15, 0) };

        MealPeriodCalculator.InitialTick(periods, minutesPerTick: 10, leadMinutes: 30).Should().Be(69);
    }

    [Fact]
    public void InitialTick_NoPeriods_IsZero()
    {
        MealPeriodCalculator.InitialTick(Array.Empty<MealPeriodDefinition>(), 1, 30).Should().Be(0);
    }

    [Fact]
    public void InitialTick_FractionalMinutesPerTick_StillLandsOnTheSameGameTime()
    {
        var periods = new[] { Period("lunch", 12, 0, 15, 0) };

        // 4 ticks per real second at 1 game minute per second = 0.25 minutes per tick:
        // 11:30 is 690 game minutes = 2760 ticks.
        var tick = MealPeriodCalculator.InitialTick(periods, minutesPerTick: 0.25, leadMinutes: 30);

        tick.Should().Be(2760);
        MealPeriodCalculator.MinutesIntoDay(0.25, tick).Should().Be(690);
    }

    [Fact]
    public void FindActive_FractionalMinutesPerTick_UsesGameTimeNotTickCount()
    {
        var periods = new[] { Period("lunch", 12, 0, 15, 0) };

        MealPeriodCalculator.FindActive(periods, 0.25, 2879).Should().BeNull();       // 11:59.75
        MealPeriodCalculator.FindActive(periods, 0.25, 2880)!.MealPeriodId.Should().Be("lunch"); // 12:00
    }

    private static MealPeriodDefinition Period(string id, int startHour, int startMinute, int endHour, int endMinute) => new()
    {
        MealPeriodId = id,
        StartTime = new TimeOfDay(startHour, startMinute),
        EndTime = new TimeOfDay(endHour, endMinute)
    };
}
