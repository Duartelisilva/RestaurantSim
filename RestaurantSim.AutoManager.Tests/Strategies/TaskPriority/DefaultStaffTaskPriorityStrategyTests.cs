using FluentAssertions;
using RestaurantSim.AutoManager.Strategies.TaskPriority;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.AutoManager.Tests.Strategies.TaskPriority;

public class DefaultStaffTaskPriorityStrategyTests
{
    private readonly DefaultStaffTaskPriorityStrategy _strategy = new();
    private const float DirtyThreshold = 30f;
    private const float AlmostDirtyThreshold = 60f;

    [Fact]
    public void DecideNextBusserTask_PicksUrgentBathroom_OverDirtyTable()
    {
        // mechanics doc §13's revised tiered ordering — urgent bathroom outranks tables.
        var restaurant = BuildRestaurant();
        AddDirtyTable(restaurant, tableId: 1);
        var urgentBathroom = AddBathroom(restaurant, bathroomId: 1, cleanlinessScore: 20f, maxConcurrentCleaners: 1);

        var decision = _strategy.DecideNextBusserTask(restaurant, DirtyThreshold, AlmostDirtyThreshold);

        decision.Should().Be((TaskType.CleanBathroom, urgentBathroom.BathroomId));
    }

    [Fact]
    public void DecideNextBusserTask_PicksDirtyTable_OverMerelyEligibleBathroom()
    {
        var restaurant = BuildRestaurant();
        var table = AddDirtyTable(restaurant, tableId: 1);
        AddBathroom(restaurant, bathroomId: 1, cleanlinessScore: 50f, maxConcurrentCleaners: 1); // eligible, not urgent

        var decision = _strategy.DecideNextBusserTask(restaurant, DirtyThreshold, AlmostDirtyThreshold);

        decision.Should().Be((TaskType.CleanTable, table.TableId));
    }

    [Fact]
    public void DecideNextBusserTask_PicksEligibleBathroom_WhenNoTablesOrUrgentBathroomsExist()
    {
        var restaurant = BuildRestaurant();
        var bathroom = AddBathroom(restaurant, bathroomId: 1, cleanlinessScore: 50f, maxConcurrentCleaners: 1);

        var decision = _strategy.DecideNextBusserTask(restaurant, DirtyThreshold, AlmostDirtyThreshold);

        decision.Should().Be((TaskType.CleanBathroom, bathroom.BathroomId));
    }

    [Fact]
    public void DecideNextBusserTask_ReturnsNull_WhenNothingNeedsCleaning()
    {
        var restaurant = BuildRestaurant();
        AddBathroom(restaurant, bathroomId: 1, cleanlinessScore: 100f, maxConcurrentCleaners: 1);

        var decision = _strategy.DecideNextBusserTask(restaurant, DirtyThreshold, AlmostDirtyThreshold);

        decision.Should().BeNull();
    }

    [Fact]
    public void DecideNextBusserTask_SkipsUrgentBathroom_WhenMaxConcurrentCleanersReached()
    {
        var restaurant = BuildRestaurant();
        var bathroom = AddBathroom(restaurant, bathroomId: 1, cleanlinessScore: 20f, maxConcurrentCleaners: 1);
        AddBusyCleaner(restaurant, bathroomId: bathroom.BathroomId);

        var decision = _strategy.DecideNextBusserTask(restaurant, DirtyThreshold, AlmostDirtyThreshold);

        decision.Should().BeNull(); // fully staffed already, nothing else to do
    }

    private static Table AddDirtyTable(Restaurant restaurant, int tableId)
    {
        var table = new Table
        {
            TableId = tableId, Position = new GridPosition(0, 0), Hitbox = new GridBounds(1, 1),
            Shape = new TableShape { ShapeId = "standard_2top", SeatDefinitions = Array.Empty<SeatDefinition>(), CanMerge = false },
            Seats = Array.Empty<Seat>(), IsClean = false
        };
        restaurant.Floor.Tables.Add(table);
        return table;
    }

    private static Bathroom AddBathroom(Restaurant restaurant, int bathroomId, float cleanlinessScore, int maxConcurrentCleaners)
    {
        var bathroom = new Bathroom
        {
            BathroomId = bathroomId, Gender = BathroomGender.Male, Position = new GridPosition(5, 0), Hitbox = new GridBounds(1, 1),
            Capacity = 1, MaxConcurrentCleaners = maxConcurrentCleaners, CleanlinessScore = cleanlinessScore
        };
        restaurant.Floor.Bathrooms.Add(bathroom);
        return bathroom;
    }

    private static void AddBusyCleaner(Restaurant restaurant, int bathroomId)
    {
        restaurant.StaffRoster.Staff.Add(new StaffMember
        {
            StaffId = 99, Name = "Busser Existing", Role = StaffRole.Busser, State = StaffState.Busy,
            CurrentTaskType = TaskType.CleanBathroom, CurrentTaskTargetId = bathroomId,
            Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
        });
    }

    private static Restaurant BuildRestaurant() => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner",
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
