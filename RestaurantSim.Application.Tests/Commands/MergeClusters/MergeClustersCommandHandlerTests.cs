using System.Numerics;
using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.MergeClusters;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.MergeClusters;

public class MergeClustersCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly MergeClustersCommandHandler _handler;

    public MergeClustersCommandHandlerTests()
    {
        _handler = new MergeClustersCommandHandler(_repository, _publisher);
    }

    [Fact]
    public async Task Handle_MergesTouchingFreeClusters_IntoTheLowestIdClusterAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 3);

        var result = await RunAsync(restaurant, 1, 2);

        result.IsSuccess.Should().BeTrue();
        var floor = restaurant.Floor;
        floor.Clusters.Select(c => c.ClusterId).Should().BeEquivalentTo(new[] { 1, 3 });

        var merged = floor.Clusters.Single(c => c.ClusterId == 1);
        merged.TableIds.Should().Equal(1, 2);
        merged.Capacity.Should().Be(4);
        merged.IsMergedOnDemand.Should().BeTrue();
        merged.MergedFrom!.Select(c => c.ClusterId).Should().Equal(1, 2);
        floor.Tables.Where(t => t.TableId is 1 or 2).Should().OnlyContain(t => t.ClusterId == 1);

        await _repository.Received(1).SaveAsync(restaurant, Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(
            Arg.Is<ClustersMerged>(e => e.ClusterId == 1 && e.Capacity == 4 && e.MergedClusterIds.SequenceEqual(new[] { 1, 2 })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheMergedClusterGoesToTheLeastLoadedWaiterAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        restaurant.StaffRoster.Staff.Add(Waiter(1));
        restaurant.StaffRoster.Staff.Add(Waiter(2));

        // Waiter 1 is busy with an occupied cluster 3; waiter 2 has nobody.
        var busy = restaurant.Floor.Clusters.Single(c => c.ClusterId == 3);
        busy.OccupiedBy = 99;
        busy.AssignedWaiterId = 1;
        restaurant.Floor.Clusters.Single(c => c.ClusterId == 1).AssignedWaiterId = 1;

        await RunAsync(restaurant, 1, 2);

        restaurant.Floor.Clusters.Single(c => c.ClusterId == 1).AssignedWaiterId.Should().Be(2);
    }

    [Fact]
    public async Task Handle_MergingAMergedClusterAgain_StillRemembersTheOriginalClustersAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 3);

        await RunAsync(restaurant, 1, 2);
        await RunAsync(restaurant, 1, 3);

        var merged = restaurant.Floor.Clusters.Single();
        merged.TableIds.Should().Equal(1, 2, 3);
        merged.Capacity.Should().Be(6);
        merged.MergedFrom!.Select(c => c.ClusterId).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Handle_ClustersThatDoNotTouch_AreRejectedAndNothingChangesAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 3);

        var result = await RunAsync(restaurant, 1, 3);

        result.IsFailure.Should().BeTrue();
        restaurant.Floor.Clusters.Should().HaveCount(3);
        restaurant.Floor.Clusters.Should().OnlyContain(c => !c.IsMergedOnDemand);
        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(ClustersMerged)!, default);
    }

    [Fact]
    public async Task Handle_AnOccupiedCluster_IsRejectedAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 2);
        restaurant.Floor.Clusters.Single(c => c.ClusterId == 2).OccupiedBy = 5;

        var result = await RunAsync(restaurant, 1, 2);

        result.IsFailure.Should().BeTrue();
        restaurant.Floor.Clusters.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_AClusterThatNeedsCleaning_IsRejectedAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 2);
        restaurant.Floor.Tables.Single(t => t.TableId == 2).IsClean = false;

        (await RunAsync(restaurant, 1, 2)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownCluster_IsRejectedAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 2);

        (await RunAsync(restaurant, 1, 42)).IsFailure.Should().BeTrue();
    }

    private async Task<RestaurantSim.Application.Common.Result> RunAsync(Restaurant restaurant, params int[] clusterIds)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        return await _handler.Handle(new MergeClustersCommand(restaurant.RestaurantId, clusterIds), CancellationToken.None);
    }

    // `tableCount` standalone 2-tops side by side (touching), table n in cluster n.
    private static Restaurant BuildRestaurant(int tableCount)
    {
        var shape = new TableShape
        {
            ShapeId = "standard_2top",
            CanMerge = true,
            SeatDefinitions = new List<SeatDefinition>
            {
                new(0, new Vector2(0, -0.5f), Orientation.South),
                new(1, new Vector2(0, 0.5f), Orientation.North)
            }
        };

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = "default_diner",
            SimulationTick = 100,
            Floor = new FloorState(),
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        for (var id = 1; id <= tableCount; id++)
        {
            restaurant.Floor.Tables.Add(new Table
            {
                TableId = id,
                Position = new GridPosition(id - 1, 0),
                Hitbox = new GridBounds(1, 1),
                Shape = shape,
                Seats = shape.SeatDefinitions.Select(d => new Seat { SeatIndex = d.SeatIndex, RelativeOffset = d.RelativeOffset, Orientation = d.Orientation }).ToList(),
                ClusterId = id
            });

            restaurant.Floor.Clusters.Add(new TableCluster { ClusterId = id, TableIds = new List<int> { id }, Capacity = 2 });
        }

        return restaurant;
    }

    private static StaffMember Waiter(int id) => new()
    {
        StaffId = id, Name = "Waiter " + id, Role = StaffRole.Waiter, State = StaffState.Idle,
        Stats = new StaffStats(), AssignedMealPeriods = new[] { "lunch" }
    };
}
