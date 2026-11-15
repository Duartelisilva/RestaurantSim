using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.MergeClusters;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.Tests.Support;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.MergeClusters;

// Tables slide together: merging works across a small free gap, the staying table is chosen
// by the mechanics doc's rules, and the moved tables keep the cluster Moving for a while.
public class MergeClustersMovableTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly MergeClustersCommandHandler _handler;

    public MergeClustersMovableTests()
    {
        _handler = new MergeClustersCommandHandler(_repository, _presetRepository, _publisher);
    }

    [Fact]
    public async Task TwoTablesWithAGap_OneSlidesToTheOther_AndTheClusterIsMovingUntilItArrivesAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2, 4);

        var result = await RunAsync(restaurant, null, 1, 2);

        result.IsSuccess.Should().BeTrue();
        var stayer = restaurant.Floor.Tables.Single(t => t.TableId == 1);
        var slider = restaurant.Floor.Tables.Single(t => t.TableId == 2);
        stayer.Position.Should().Be(new GridPosition(0, 0));
        stayer.IsMoving.Should().BeFalse();
        slider.Position.Should().Be(new GridPosition(1, 0));
        slider.MoveFrom.Should().Be(new GridPosition(2, 0));
        slider.MoveEndTick.Should().Be(102); // tick 100 + 1 cell x 2 s (default) x 1 tick/s
        slider.HomePosition.Should().Be(new GridPosition(2, 0));

        var merged = restaurant.Floor.Clusters.Single(c => c.ClusterId == 1);
        merged.DeriveState(restaurant.Floor.Tables, restaurant.CustomerService.Reservations, 100).Should().Be(TableClusterState.Moving);
        merged.DeriveState(restaurant.Floor.Tables, restaurant.CustomerService.Reservations, 102).Should().Be(TableClusterState.Moving);

        await _publisher.Received(1).Publish(
            Arg.Is<ClustersMerged>(e => e.DurationTicks == 2
                && e.Moves.Count == 1
                && e.Moves[0].TableId == 2
                && e.Moves[0].From == new GridPosition(2, 0)
                && e.Moves[0].To == new GridPosition(1, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheDurationFollowsTheConfiguredSecondsPerCellAndTheTickRateAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);

        await RunAsync(restaurant, TestRestaurantConfig.Build(secondsPerCell: 3f, ticksPerSecond: 2), 1, 2);

        restaurant.Floor.Tables.Single(t => t.TableId == 2).MoveEndTick.Should().Be(106); // 100 + 1 x 3 s x 2 ticks/s
    }

    [Fact]
    public async Task ThreeInARow_TheMiddleOneStaysAndTheOuterOnesSlideInAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2, 4);

        await RunAsync(restaurant, null, 1, 2, 3);

        restaurant.Floor.Tables.Select(t => t.Position.X).Should().Equal(1, 2, 3);
        restaurant.Floor.Tables.Single(t => t.TableId == 2).IsMoving.Should().BeFalse();
        restaurant.Floor.Clusters.Should().ContainSingle().Which.Capacity.Should().Be(6);
    }

    [Fact]
    public async Task TwoTables_TheTableOfTheLessBusyWaiterStays_AndThatWaiterOwnsTheMergedClusterAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2, 4);
        restaurant.Floor.Clusters.Single(c => c.ClusterId == 1).AssignedWaiterId = 1;
        restaurant.Floor.Clusters.Single(c => c.ClusterId == 2).AssignedWaiterId = 2;
        var busy = restaurant.Floor.Clusters.Single(c => c.ClusterId == 3);
        busy.AssignedWaiterId = 1;
        busy.OccupiedBy = 99; // waiter 1 has a seated group, waiter 2 has none

        await RunAsync(restaurant, null, 1, 2);

        restaurant.Floor.Clusters.Single(c => c.ClusterId == 1).AssignedWaiterId.Should().Be(2);
        restaurant.Floor.Tables.Single(t => t.TableId == 2).IsMoving.Should().BeFalse(); // table 2 is in waiter 2's area: it stays
        restaurant.Floor.Tables.Single(t => t.TableId == 1).Position.Should().Be(new GridPosition(1, 0));
    }

    [Fact]
    public async Task TablesAlreadyTouching_MergeWithoutAnyMoveAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 1);

        var result = await RunAsync(restaurant, null, 1, 2);

        result.IsSuccess.Should().BeTrue();
        restaurant.Floor.Tables.Should().OnlyContain(t => !t.IsMoving);
        await _publisher.Received(1).Publish(
            Arg.Is<ClustersMerged>(e => e.Moves.Count == 0 && e.DurationTicks == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TablesTooFarApart_AreRejectedAndNothingMovesAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 3);

        var result = await RunAsync(restaurant, null, 1, 2);

        result.IsFailure.Should().BeTrue();
        restaurant.Floor.Tables.Select(t => t.Position.X).Should().Equal(0, 3);
        restaurant.Floor.Clusters.Should().HaveCount(2);
        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(ClustersMerged)!, default);
    }

    [Fact]
    public async Task AGapCellTakenBySomethingElse_IsRejectedAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);
        restaurant.Floor.Freezers.Add(new Freezer { FreezerId = 1, Position = new GridPosition(1, 0), Hitbox = new GridBounds(1, 1) });

        var result = await RunAsync(restaurant, null, 1, 2);

        result.IsFailure.Should().BeTrue();
        restaurant.Floor.Tables.Select(t => t.Position.X).Should().Equal(0, 2);
    }

    [Fact]
    public async Task AFloorWithNoGapLimit_StillRequiresTouchingTablesAsync()
    {
        var restaurant = TestFloors.Row(0, 0, 2);

        (await RunAsync(restaurant, null, 1, 2)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AMovingCluster_CannotBeMergedFurtherUntilItArrivesAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2, 4);
        await RunAsync(restaurant, null, 1, 2);

        (await RunAsync(restaurant, null, 1, 3)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutAConfig_TheMergeIsRejectedAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns((RestaurantConfig?)null);

        var result = await _handler.Handle(new MergeClustersCommand(restaurant.RestaurantId, new[] { 1, 2 }), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private async Task<Result> RunAsync(Restaurant restaurant, RestaurantConfig? config, params int[] clusterIds)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(config ?? TestRestaurantConfig.Build());
        return await _handler.Handle(new MergeClustersCommand(restaurant.RestaurantId, clusterIds), CancellationToken.None);
    }
}
