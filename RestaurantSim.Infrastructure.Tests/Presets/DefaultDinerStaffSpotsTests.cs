using FluentAssertions;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using RestaurantSim.Infrastructure.Presets;

namespace RestaurantSim.Infrastructure.Tests.Presets;

// Idle spots are preset data; a typo (a spot on a table, two people on one cell, too few
// spots for the roster) would only show up when a restaurant is created.
public class DefaultDinerStaffSpotsTests
{
    [Fact]
    public void EveryRoleOfTheDefaultRoster_HasEnoughSpots()
    {
        var layout = DefaultDinerLayoutSeed.Create();

        foreach (var (role, count) in DefaultDinerConfigSeed.Create().StaffRosterSeed)
            layout.StaffSpots.GetValueOrDefault(role).Should().NotBeNull($"{role} needs spots")
                .And.HaveCountGreaterThanOrEqualTo(count, $"the roster has {count} {role}(s)");
    }

    [Fact]
    public void NoTwoSpotsAreTheSame_AndNoSpotIsOnFurniture()
    {
        var layout = DefaultDinerLayoutSeed.Create();
        var spots = layout.StaffSpots.Values.SelectMany(s => s).ToList();
        var furniture = new List<IPlaceable>();
        furniture.AddRange(layout.Tables);
        furniture.AddRange(layout.KitchenStations);
        furniture.AddRange(layout.HeatLamps);
        furniture.AddRange(layout.Freezers);
        furniture.AddRange(layout.Bathrooms);
        var blocked = furniture.SelectMany(Footprint.Cells).ToHashSet();

        spots.Should().OnlyHaveUniqueItems();
        spots.Should().NotContain(spot => blocked.Contains(spot));
    }

    [Fact]
    public void TheDefaultSpots_AreThoseAgreedWithTheOwner()
    {
        var spots = DefaultDinerLayoutSeed.Create().StaffSpots;

        spots[StaffRole.Host].Should().Equal(new GridPosition(8, 3));
        spots[StaffRole.Waiter].Should().Equal(new GridPosition(5, 3), new GridPosition(5, 4));
        spots[StaffRole.HeadChef].Should().Equal(new GridPosition(4, 5));
        spots[StaffRole.Busser].Should().Equal(new GridPosition(6, 5));
        spots[StaffRole.Cook].Should().Equal(new GridPosition(0, 5), new GridPosition(1, 5), new GridPosition(3, 5));
    }
}
