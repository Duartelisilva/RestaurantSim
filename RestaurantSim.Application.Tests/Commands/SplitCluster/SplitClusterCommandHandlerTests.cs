using System.Numerics;
using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.MergeClusters;
using RestaurantSim.Application.Commands.SplitCluster;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SplitCluster;

public class SplitClusterCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SplitClusterCommandHandler _splitHandler;
    private readonly MergeClustersCommandHandler _mergeHandler;

    public SplitClusterCommandHandlerTests()
    {
        _splitHandler = new SplitClusterCommandHandler(_repository, _publisher);
        _mergeHandler = new MergeClustersCommandHandler(_repository, _publisher);
    }

    [Fact]
    public async Task Handle_PutsTheOriginalClustersBackExactlyAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        restaurant.Floor.Clusters.Single(c => c.ClusterId == 1).AssignedWaiterId = 7;
        restaurant.Floor.Clusters.Single(c => c.ClusterId == 2).AssignedWaiterId = 8;
        await MergeAsync(restaurant, 1, 2);

        var result = await SplitAsync(restaurant, 1);

        result.IsSuccess.Should().BeTrue();
        var clusters = restaurant.Floor.Clusters.OrderBy(c => c.ClusterId).ToList();
        clusters.Select(c => c.ClusterId).Should().Equal(1, 2, 3);
        clusters.Select(c => c.Capacity).Should().Equal(2, 2, 2);
        clusters.Should().OnlyContain(c => !c.IsMergedOnDemand && c.OccupiedBy == null);
        clusters.Select(c => c.AssignedWaiterId).Should().Equal(7, 8, null);
        restaurant.Floor.Tables.Select(t => t.ClusterId).Should().Equal(1, 2, 3);

        await _publisher.Received(1).Publish(
            Arg.Is<ClusterSplit>(e => e.MergedClusterId == 1 && e.Restored.Select(r => r.ClusterId).SequenceEqual(new[] { 1, 2 })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ANestedMerge_IsFullyUndoneInOneSplitAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 3);
        await MergeAsync(restaurant, 1, 2);
        await MergeAsync(restaurant, 1, 3);

        var result = await SplitAsync(restaurant, 1);

        result.IsSuccess.Should().BeTrue();
        restaurant.Floor.Clusters.Select(c => c.ClusterId).OrderBy(id => id).Should().Equal(1, 2, 3);
        restaurant.Floor.Clusters.Should().OnlyContain(c => c.Capacity == 2 && c.TableIds.Count == 1);
    }

    [Fact]
    public async Task Handle_AClusterAuthoredPreMerged_CannotBeSplitAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 2);
        var authored = restaurant.Floor.Clusters.Single(c => c.ClusterId == 1);
        authored.TableIds = new List<int> { 1, 2 };   // pre-merged in the layout: MergedFrom stays null
        authored.Capacity = 4;
        restaurant.Floor.Clusters.RemoveAll(c => c.ClusterId == 2);

        var result = await SplitAsync(restaurant, 1);

        result.IsFailure.Should().BeTrue();
        restaurant.Floor.Clusters.Single().TableIds.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Handle_AnOccupiedMergedCluster_CannotBeSplitAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 2);
        await MergeAsync(restaurant, 1, 2);
        restaurant.Floor.Clusters.Single().OccupiedBy = 5;

        (await SplitAsync(restaurant, 1)).IsFailure.Should().BeTrue();
        restaurant.Floor.Clusters.Single().IsMergedOnDemand.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AMergedClusterStillBeingCleaned_CannotBeSplitYetAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 2);
        await MergeAsync(restaurant, 1, 2);
        restaurant.Floor.Tables.First().IsClean = false;

        (await SplitAsync(restaurant, 1)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownCluster_IsRejectedAsync()
    {
        var restaurant = BuildRestaurant(tableCount: 2);

        (await SplitAsync(restaurant, 42)).IsFailure.Should().BeTrue();
    }

    private async Task<RestaurantSim.Application.Common.Result> MergeAsync(Restaurant restaurant, params int[] ids)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        return await _mergeHandler.Handle(new MergeClustersCommand(restaurant.RestaurantId, ids), CancellationToken.None);
    }

    private async Task<RestaurantSim.Application.Common.Result> SplitAsync(Restaurant restaurant, int clusterId)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        return await _splitHandler.Handle(new SplitClusterCommand(restaurant.RestaurantId, clusterId), CancellationToken.None);
    }

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
}
