using FluentAssertions;
using RestaurantSim.Client.Components;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Tests.Components;

public class StaffLabelsTests
{
    [Theory]
    [InlineData(StaffStateNames.OffShift, null, null, "off shift")]
    [InlineData(StaffStateNames.Idle, null, null, "idle")]
    [InlineData(StaffStateNames.Resting, null, null, "resting")]
    [InlineData(StaffStateNames.Busy, TaskTypeNames.CleanTable, 3, "cleaning table 3")]
    [InlineData(StaffStateNames.Busy, TaskTypeNames.CleanBathroom, 2, "cleaning bathroom 2")]
    [InlineData(StaffStateNames.Busy, TaskTypeNames.CookItem, 14, "cooking item 14")]
    [InlineData(StaffStateNames.Busy, TaskTypeNames.DeliverOrderItem, 5, "delivering item 5")]
    [InlineData(StaffStateNames.Busy, TaskTypeNames.SeatGroup, 7, "seating group 7")]
    [InlineData(StaffStateNames.Busy, TaskTypeNames.AcknowledgeCall, 1, "answering call 1")]
    [InlineData(StaffStateNames.Busy, null, null, "busy")]
    [InlineData("Dancing", null, null, "Dancing")]
    public void Describe_WordsTheStateAndTask(string state, string? task, int? target, string expected)
    {
        StaffLabels.Describe(Staff(state, task, target)).Should().Be(expected);
    }

    [Fact]
    public void Summary_ShowsNameStateAndFatiguePercent()
    {
        var staff = Staff(StaffStateNames.Busy, TaskTypeNames.CleanTable, 3) with { Name = "Busser 1", Fatigue = 0.12f };

        StaffLabels.Summary(staff).Should().Be("Busser 1 — cleaning table 3 · fatigue 12%");
    }

    [Theory]
    [InlineData(StaffRoleNames.Waiter, "W")]
    [InlineData(StaffRoleNames.Host, "H")]
    [InlineData(StaffRoleNames.Cook, "C")]
    [InlineData(StaffRoleNames.HeadChef, "HC")]
    [InlineData(StaffRoleNames.Busser, "B")]
    [InlineData("Sommelier", "S")]
    public void Initial_IsAShortLetterForTheRole(string role, string expected)
    {
        StaffLabels.Initial(role).Should().Be(expected);
    }

    [Fact]
    public void CssClasses_AreTheLowercasedNamesWithAPrefix()
    {
        StaffLabels.RoleClass(StaffRoleNames.HeadChef).Should().Be("role-headchef");
        StaffLabels.StateClass(StaffStateNames.OffShift).Should().Be("staff-offshift");
    }

    private static StaffDto Staff(string state, string? task, int? target) =>
        new(1, "Waiter 1", StaffRoleNames.Waiter, state, new GridPointDto(5, 3), task, target, 0f);
}
