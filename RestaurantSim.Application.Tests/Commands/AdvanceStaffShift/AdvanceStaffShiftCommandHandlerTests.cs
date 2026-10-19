using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Commands.AdvanceStaffShift;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.AdvanceStaffShift;

public class AdvanceStaffShiftCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly AdvanceStaffShiftCommandHandler _handler;

    public AdvanceStaffShiftCommandHandlerTests()
    {
        _handler = new AdvanceStaffShiftCommandHandler(_repository, _presetRepository);
    }

    [Fact]
    public async Task Handle_ClocksIn_WhenWithinShiftAndCurrentlyOffShift()
    {
        var restaurant = BuildRestaurant(simulationTick: 800); // 13:20, within lunch 12:00-15:00
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.OffShift);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.State.Should().Be(StaffState.Idle);
    }

    [Fact]
    public async Task Handle_ClocksOut_WhenPastShiftEndAndNoDemand()
    {
        var restaurant = BuildRestaurant(simulationTick: 950); // 15:50, past lunch end (900)
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.Idle);
        staff.Stats.Set(StatType.Fatigue, 0.5f);
        SetupConfig(restaurant);
        // No ActiveGroups/Queue -> no demand for Waiter.

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.State.Should().Be(StaffState.OffShift);
        staff.Stats.Get(StatType.Fatigue).Should().Be(0f); // reset on the transition
    }

    [Fact]
    public async Task Handle_BlocksClockOut_WhenDemandExistsAndNoOtherCoverage()
    {
        var restaurant = BuildRestaurant(simulationTick: 950);
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.Idle);
        AddActiveGroupDemand(restaurant); // creates demand for Waiter/Host
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.State.Should().Be(StaffState.Idle); // held, coverage-gated
    }

    [Fact]
    public async Task Handle_AllowsClockOut_WhenAnotherStaffOfSameRoleCovers()
    {
        var restaurant = BuildRestaurant(simulationTick: 950);
        var staffA = AddStaff(restaurant, StaffRole.Waiter, StaffState.Idle, staffId: 1);
        var staffB = AddStaff(restaurant, StaffRole.Waiter, StaffState.Idle, staffId: 2); // covers
        AddActiveGroupDemand(restaurant);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staffA.State.Should().Be(StaffState.OffShift);
        // staffB is also past ShiftEnd, but no THIRD staff covers for them —
        // held by the same rule, applied symmetrically.
        staffB.State.Should().Be(StaffState.Idle);
    }

    [Fact]
    public async Task Handle_DoesNotClockOut_BusyStaff_RegardlessOfShiftEnd()
    {
        var restaurant = BuildRestaurant(simulationTick: 950);
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.Busy);
        SetupConfig(restaurant);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.State.Should().Be(StaffState.Busy); // finishes task first, untouched here
    }

    [Fact]
    public async Task Handle_AccruesFatigue_AtOvertimeRate_WhenHeldPastShiftEnd()
    {
        var restaurant = BuildRestaurant(simulationTick: 950);
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.Idle);
        AddActiveGroupDemand(restaurant); // held, coverage-gated
        var config = SetupConfig(restaurant, baseFatigueRate: 0.01f, overtimeMultiplier: 2f);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.Stats.Get(StatType.Fatigue).Should().Be(0.02f); // 0.01 x 2 overtime multiplier
    }

    [Fact]
    public async Task Handle_AccruesFatigue_AtNormalRate_WithinShift()
    {
        var restaurant = BuildRestaurant(simulationTick: 800); // within lunch
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.Idle);
        SetupConfig(restaurant, baseFatigueRate: 0.01f, overtimeMultiplier: 2f);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.Stats.Get(StatType.Fatigue).Should().Be(0.01f); // normal rate, not overtime
    }

    [Fact]
    public async Task Handle_DoesNotAccrueFatigue_WhileOffShift()
    {
        var restaurant = BuildRestaurant(simulationTick: 100); // well before any shift
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.OffShift);
        SetupConfig(restaurant, baseFatigueRate: 0.01f, overtimeMultiplier: 2f);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.Stats.Get(StatType.Fatigue).Should().Be(0f);
    }

    [Fact]
    public async Task Handle_ClampsFatigueAtOne()
    {
        var restaurant = BuildRestaurant(simulationTick: 800);
        var staff = AddStaff(restaurant, StaffRole.Waiter, StaffState.Idle);
        staff.Stats.Set(StatType.Fatigue, 0.999f);
        SetupConfig(restaurant, baseFatigueRate: 0.01f, overtimeMultiplier: 2f);

        await _handler.Handle(new AdvanceStaffShiftCommand(restaurant.RestaurantId), CancellationToken.None);

        staff.Stats.Get(StatType.Fatigue).Should().BeLessThanOrEqualTo(1f);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new AdvanceStaffShiftCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static void AddActiveGroupDemand(Restaurant restaurant)
    {
        restaurant.CustomerService.ActiveGroups.Add(new RestaurantSim.Domain.CustomerService.CustomerGroup
        {
            GroupId = 1, PartySize = 1, Members = new List<RestaurantSim.Domain.CustomerService.Customer>(), ArrivalTick = 0
        });
    }

    private static StaffMember AddStaff(Restaurant restaurant, StaffRole role, StaffState state, int staffId = 1)
    {
        var stats = new StaffStats();
        stats.Set(StatType.Fatigue, 0f);   // <-- add this line
        var staff = new StaffMember
        {
            StaffId = staffId,
            Name = $"{role} {staffId}",
            Role = role,
            State = state,
            Stats = stats,
            AssignedMealPeriods = new[] { "lunch" }
        };
        restaurant.StaffRoster.Staff.Add(staff);
        return staff;
    }

    private RestaurantConfig SetupConfig(Restaurant restaurant, float? baseFatigueRate = null, float? overtimeMultiplier = null)
    {
        var config = new RestaurantConfig
        {
            ConfigId = "default_diner", StartingCash = 5000m, GameMinutesPerRealSecond = 1, TicksPerRealSecond = 1,
            TableLayoutId = "default_diner_layout", StaffRosterSeed = Array.Empty<(StaffRole, int)>(),
            MenuItemIds = Array.Empty<int>(), ReputationStart = 20f,
            QueueWaitMoodDecayPerSecond = -0.4f, CourseWaitMoodDecayPerSecond = -0.3f, NaturalMoodRecoveryPerSecond = 0.5f,
            ProbabilityCheckpointIntervalSeconds = 3f, PathReplanMaxWaitSeconds = 5f, MaxCallAttempts = 3,
            ActiveMealPeriods = new List<MealPeriodDefinition>
            {
                new() { MealPeriodId = "lunch", StartTime = new TimeOfDay(12, 0), EndTime = new TimeOfDay(15, 0) }
            },
            EventModuleIds = Array.Empty<string>(), TipBaseRate = 50f, TipMoodExponent = 0.75f,
            MaxQueueLength = 12, BaseArrivalIntervalSeconds = 120f,
            BaseFatigueAccrualPerSecond = baseFatigueRate,
            OvertimeFatigueMultiplier = overtimeMultiplier
        };

        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        return config;
    }

    private static Restaurant BuildRestaurant(long simulationTick) => new()
    {
        RestaurantId = Guid.NewGuid(), ConfigId = "default_diner", SimulationTick = simulationTick,
        Floor = new FloorState(), StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(), KitchenOps = new KitchenOpsState(), Events = new EventState()
    };
}
