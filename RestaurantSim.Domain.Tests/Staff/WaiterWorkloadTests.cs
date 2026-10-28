using FluentAssertions;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Domain.Tests.Staff;

public class WaiterWorkloadTests
{
    [Fact]
    public void LeastLoadedWaiterId_PicksTheWaiterServingTheFewestSeatedGroups()
    {
        var restaurant = Restaurant(Waiter(1), Waiter(2));
        restaurant.Floor.Clusters.Add(Cluster(10, occupiedBy: 1, waiter: 1));
        restaurant.Floor.Clusters.Add(Cluster(11, occupiedBy: 2, waiter: 1));
        restaurant.Floor.Clusters.Add(Cluster(12, occupiedBy: 3, waiter: 2));

        WaiterWorkload.LeastLoadedWaiterId(restaurant).Should().Be(2);
    }

    [Fact]
    public void LeastLoadedWaiterId_TiesGoToTheLowestId()
    {
        var restaurant = Restaurant(Waiter(2), Waiter(1));

        WaiterWorkload.LeastLoadedWaiterId(restaurant).Should().Be(1);
    }

    [Fact]
    public void LeastLoadedWaiterId_EmptyClustersDoNotCountAsWork()
    {
        var restaurant = Restaurant(Waiter(1), Waiter(2));
        restaurant.Floor.Clusters.Add(Cluster(10, occupiedBy: null, waiter: 1));
        restaurant.Floor.Clusters.Add(Cluster(11, occupiedBy: 5, waiter: 2));

        WaiterWorkload.LeastLoadedWaiterId(restaurant).Should().Be(1);
    }

    [Fact]
    public void LeastLoadedWaiterId_IgnoresWaitersOffShiftAndOtherRoles()
    {
        var restaurant = Restaurant(Waiter(1, StaffState.OffShift), Waiter(2), Cook(3));

        WaiterWorkload.LeastLoadedWaiterId(restaurant).Should().Be(2);
    }

    [Fact]
    public void LeastLoadedWaiterId_NoWaiterOnShift_IsNull()
    {
        var restaurant = Restaurant(Waiter(1, StaffState.OffShift), Cook(3));

        WaiterWorkload.LeastLoadedWaiterId(restaurant).Should().BeNull();
    }

    private static Restaurant Restaurant(params StaffMember[] staff)
    {
        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        restaurant.StaffRoster.Staff.AddRange(staff);
        return restaurant;
    }

    private static StaffMember Waiter(int id, StaffState state = StaffState.Idle) => Member(id, StaffRole.Waiter, state);

    private static StaffMember Cook(int id) => Member(id, StaffRole.Cook, StaffState.Idle);

    private static StaffMember Member(int id, StaffRole role, StaffState state) => new()
    {
        StaffId = id,
        Name = "Staff " + id,
        Role = role,
        State = state,
        Stats = new StaffStats(),
        AssignedMealPeriods = new[] { "lunch" }
    };

    private static TableCluster Cluster(int id, int? occupiedBy, int? waiter) => new()
    {
        ClusterId = id,
        TableIds = new List<int> { id },
        Capacity = 2,
        OccupiedBy = occupiedBy,
        AssignedWaiterId = waiter
    };
}
