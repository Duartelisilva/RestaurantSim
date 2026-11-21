using FluentAssertions;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Domain.Tests.Staff;

public class StaffMemberPositionTests
{
    [Fact]
    public void FirstPosition_IsAlsoTheHomePosition()
    {
        var staff = Staff(new GridPosition(5, 3));

        staff.Position.Should().Be(new GridPosition(5, 3));
        staff.HomePosition.Should().Be(new GridPosition(5, 3));
    }

    [Fact]
    public void MovingChangesPosition_NotHomePosition()
    {
        var staff = Staff(new GridPosition(5, 3));

        staff.Position = new GridPosition(2, 2);

        staff.Position.Should().Be(new GridPosition(2, 2));
        staff.HomePosition.Should().Be(new GridPosition(5, 3));
    }

    [Fact]
    public void ExplicitHomePosition_Wins()
    {
        var staff = new StaffMember
        {
            StaffId = 1, Name = "Waiter 1", Role = StaffRole.Waiter, Stats = new StaffStats(),
            AssignedMealPeriods = Array.Empty<string>(),
            Position = new GridPosition(2, 2), HomePosition = new GridPosition(5, 3)
        };

        staff.HomePosition.Should().Be(new GridPosition(5, 3));
        staff.Position.Should().Be(new GridPosition(2, 2));
    }

    private static StaffMember Staff(GridPosition position) => new()
    {
        StaffId = 1, Name = "Waiter 1", Role = StaffRole.Waiter, Stats = new StaffStats(),
        AssignedMealPeriods = Array.Empty<string>(), Position = position
    };
}
