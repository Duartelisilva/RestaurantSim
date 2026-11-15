using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.MergeClusters;
using RestaurantSim.Application.Commands.SplitCluster;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.Tests.Support;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.SplitCluster;

// Splitting puts the tables back where they came from, with the same timed slide.
public class SplitClusterMovableTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly MergeClustersCommandHandler _mergeHandler;
    private readonly SplitClusterCommandHandler _splitHandler;

    public SplitClusterMovableTests()
    {
        _mergeHandler = new MergeClustersCommandHandler(_repository, _presetRepository, _publisher);
        _splitHandler = new SplitClusterCommandHandler(_repository, _presetRepository, _publisher);
    }

    [Fact]
    public async Task AClusterStillSlidingIntoPlace_CannotBeSplitAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);
        await MergeAsync(restaurant, 1, 2);

        (await SplitAsync(restaurant, 1)).IsFailure.Should().BeTrue();
        restaurant.Floor.Clusters.Should().ContainSingle();
    }

    [Fact]
    public async Task OnceArrived_SplitSlidesTheTablesBackHome_AndTheRestoredClustersAreMovingAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2, 4);
        await MergeAsync(restaurant, 1, 2);
        Arrive(restaurant);
        restaurant.SimulationTick = 110;

        var result = await SplitAsync(restaurant, 1);

        result.IsSuccess.Should().BeTrue();
        var slider = restaurant.Floor.Tables.Single(t => t.TableId == 2);
        slider.Position.Should().Be(new GridPosition(2, 0));
        slider.MoveFrom.Should().Be(new GridPosition(1, 0));
        slider.MoveEndTick.Should().Be(112);
        restaurant.Floor.Tables.Single(t => t.TableId == 1).IsMoving.Should().BeFalse();

        restaurant.Floor.Clusters.Select(c => c.ClusterId).Should().BeEquivalentTo(new[] { 1, 2, 3 });
        var restoredSecond = restaurant.Floor.Clusters.Single(c => c.ClusterId == 2);
        restoredSecond.DeriveState(restaurant.Floor.Tables, restaurant.CustomerService.Reservations, 110).Should().Be(TableClusterState.Moving);
        restoredSecond.AnchorPosition.Should().Be(new GridPosition(2, 0)); // anchor reads the table's home position

        await _publisher.Received(1).Publish(
            Arg.Is<ClusterSplit>(e => e.DurationTicks == 2
                && e.Moves.Count == 1
                && e.Moves[0].TableId == 2
                && e.Moves[0].To == new GridPosition(2, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AHomeCellTakenInTheMeantime_RejectsTheSplit_AndNothingMovesAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2);
        await MergeAsync(restaurant, 1, 2);
        Arrive(restaurant);
        restaurant.Floor.Freezers.Add(new Freezer { FreezerId = 1, Position = new GridPosition(2, 0), Hitbox = new GridBounds(1, 1) });

        var result = await SplitAsync(restaurant, 1);

        result.IsFailure.Should().BeTrue();
        restaurant.Floor.Tables.Single(t => t.TableId == 2).Position.Should().Be(new GridPosition(1, 0));
        restaurant.Floor.Tables.Should().OnlyContain(t => !t.IsMoving);
        restaurant.Floor.Clusters.Should().ContainSingle().Which.IsMergedOnDemand.Should().BeTrue();
    }

    [Fact]
    public async Task TablesThatNeverMoved_SplitWithoutAnyMoveAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 1);
        await MergeAsync(restaurant, 1, 2);

        var result = await SplitAsync(restaurant, 1);

        result.IsSuccess.Should().BeTrue();
        restaurant.Floor.Tables.Should().OnlyContain(t => !t.IsMoving);
        await _publisher.Received(1).Publish(
            Arg.Is<ClusterSplit>(e => e.Moves.Count == 0 && e.DurationTicks == 0), Arg.Any<CancellationToken>());
    }

    // What AdvanceTableMovesCommand does once the end tick is reached.
    private static void Arrive(Restaurant restaurant)
    {
        foreach (var table in restaurant.Floor.Tables)
            table.CompleteMove();
    }

    private async Task<Result> MergeAsync(Restaurant restaurant, params int[] ids)
    {
        Setup(restaurant);
        return await _mergeHandler.Handle(new MergeClustersCommand(restaurant.RestaurantId, ids), CancellationToken.None);
    }

    private async Task<Result> SplitAsync(Restaurant restaurant, int clusterId)
    {
        Setup(restaurant);
        return await _splitHandler.Handle(new SplitClusterCommand(restaurant.RestaurantId, clusterId), CancellationToken.None);
    }

    private void Setup(Restaurant restaurant)
    {
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(TestRestaurantConfig.Build());
    }
}
