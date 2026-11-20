using FluentAssertions;
using NSubstitute;
using RestaurantSim.Application.Queries.GetRestaurantSnapshot;
using RestaurantSim.Application.Tests.Support;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Floor;
using Xunit;

namespace RestaurantSim.Application.Tests.Queries.GetRestaurantSnapshot;

// A client that joins (or reconnects) while tables are sliding must see them where they are,
// and the cluster as Moving, so it does not try to seat anyone there.
public class GetRestaurantSnapshotMovingTests
{
    [Fact]
    public async Task ASlidingTable_IsMarkedMoving_AndItsClusterStateIsMovingAsync()
    {
        var restaurant = TestFloors.Row(1, 0, 2, 4);
        restaurant.Floor.Tables[1].StartMove(new GridPosition(1, 0), endTick: 105);
        var repository = Substitute.For<IRestaurantRepository>();
        var presets = Substitute.For<IPresetRepository<RestaurantConfig>>();
        repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        presets.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(TestRestaurantConfig.Build());
        var handler = new GetRestaurantSnapshotQueryHandler(repository, presets);

        var result = await handler.Handle(new GetRestaurantSnapshotQuery(restaurant.RestaurantId), CancellationToken.None);

        var floor = result.Value!.Floor;
        floor.Tables.Select(t => t.IsMoving).Should().Equal(false, true, false);
        floor.Tables[1].Position.X.Should().Be(1);
        floor.Clusters.Select(c => c.State).Should().Equal("Free", "Moving", "Free");
    }
}
