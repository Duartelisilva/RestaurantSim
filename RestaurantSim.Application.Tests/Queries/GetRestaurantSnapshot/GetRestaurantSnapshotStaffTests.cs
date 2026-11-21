using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Queries.GetRestaurantSnapshot;
using RestaurantSim.Application.Tests.Support;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Queries.GetRestaurantSnapshot;

public class GetRestaurantSnapshotStaffTests
{
    [Fact]
    public async Task Staff_AreMappedWithPositionTaskAndFatigueAsync()
    {
        var restaurant = TestFloors.Row(0, 0);
        var stats = new StaffStats();
        stats.Set(StatType.Fatigue, 0.4f);
        restaurant.StaffRoster.Staff.Add(new StaffMember
        {
            StaffId = 7, Name = "Busser 1", Role = StaffRole.Busser, State = StaffState.Busy,
            CurrentTaskType = TaskType.CleanTable, CurrentTaskTargetId = 3,
            Stats = stats, AssignedMealPeriods = Array.Empty<string>(), Position = new GridPosition(6, 5)
        });
        restaurant.StaffRoster.Staff.Add(new StaffMember
        {
            StaffId = 8, Name = "Host 1", Role = StaffRole.Host, State = StaffState.Idle,
            Stats = new StaffStats(), AssignedMealPeriods = Array.Empty<string>(), Position = new GridPosition(8, 3)
        });
        var repository = Substitute.For<IRestaurantRepository>();
        var presets = Substitute.For<IPresetRepository<RestaurantConfig>>();
        repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        presets.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(TestRestaurantConfig.Build());
        var handler = new GetRestaurantSnapshotQueryHandler(repository, presets);

        var result = await handler.Handle(new GetRestaurantSnapshotQuery(restaurant.RestaurantId), CancellationToken.None);

        var busser = result.Value!.Staff[0];
        busser.Should().Be(new StaffDto(7, "Busser 1", StaffRoleNames.Busser, StaffStateNames.Busy,
            new GridPointDto(6, 5), TaskTypeNames.CleanTable, 3, 0.4f));
        var host = result.Value.Staff[1];
        host.TaskType.Should().BeNull();
        host.TaskTargetId.Should().BeNull();
        host.Position.Should().Be(new GridPointDto(8, 3));
    }

    [Fact]
    public void StaffRoleNames_MatchTheDomainEnumMembers()
    {
        string[] wireNames =
        [
            StaffRoleNames.Waiter, StaffRoleNames.Host, StaffRoleNames.Cook, StaffRoleNames.HeadChef, StaffRoleNames.Busser
        ];

        wireNames.Should().BeEquivalentTo(Enum.GetNames<StaffRole>());
    }

    [Fact]
    public void StaffStateNames_MatchTheDomainEnumMembers()
    {
        string[] wireNames = [StaffStateNames.Idle, StaffStateNames.Busy, StaffStateNames.Resting, StaffStateNames.OffShift];

        wireNames.Should().BeEquivalentTo(Enum.GetNames<StaffState>());
    }

    [Fact]
    public void TaskTypeNames_MatchTheDomainEnumMembers()
    {
        string[] wireNames =
        [
            TaskTypeNames.SeatGroup, TaskTypeNames.CleanTable, TaskTypeNames.CleanBathroom,
            TaskTypeNames.DeliverOrderItem, TaskTypeNames.AcknowledgeCall, TaskTypeNames.CookItem
        ];

        wireNames.Should().BeEquivalentTo(Enum.GetNames<TaskType>());
    }
}
