using System.Numerics;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Infrastructure.Presets;

// Transcribes presets.md's "default_diner_layout" section — 6 physical tables
// -> 5 TableClusters (4 standalone + 1 pre-merged 4-top), matching the doc's
// stated "max capacity 12 covers" exactly. Reuses DefaultDinerKitchenLayoutSeed
// for stations/heat lamp (built during the Kitchen phase) rather than
// duplicating that data. Bathroom door positions and Capacity/CleanlinessScore/
// MaxConcurrentCleaners were all explicitly left "TBD"/"open, pending
// playtesting" in presets.md — first-pass proposed values here, not guesses
// against the doc's own stated intent.
public static class DefaultDinerLayoutSeed
{
    public static RestaurantLayout Create()
    {
        var standard2Top = new TableShape
        {
            ShapeId = "standard_2top",
            SeatDefinitions = new List<SeatDefinition>
            {
                new(0, new Vector2(0, -0.5f), Orientation.South),
                new(1, new Vector2(0, 0.5f), Orientation.North)
            },
            CanMerge = true
        };

        var tables = new List<Table>
        {
            CreateTable(1, standard2Top, new GridPosition(0, 0), clusterId: 1),
            CreateTable(2, standard2Top, new GridPosition(2, 0), clusterId: 2),
            CreateTable(3, standard2Top, new GridPosition(0, 2), clusterId: 3),
            CreateTable(4, standard2Top, new GridPosition(2, 2), clusterId: 4),
            // Pre-merged 4-top (presets.md) — seats face N/S, so the valid merge
            // axis runs along X (entity doc §5's merge-axis rule); placed adjacent.
            CreateTable(5, standard2Top, new GridPosition(4, 0), clusterId: 5),
            CreateTable(6, standard2Top, new GridPosition(5, 0), clusterId: 5)
        };

        var clusters = new List<TableCluster>
        {
            new() { ClusterId = 1, TableIds = new List<int> { 1 }, Capacity = 2 },
            new() { ClusterId = 2, TableIds = new List<int> { 2 }, Capacity = 2 },
            new() { ClusterId = 3, TableIds = new List<int> { 3 }, Capacity = 2 },
            new() { ClusterId = 4, TableIds = new List<int> { 4 }, Capacity = 2 },
            new() { ClusterId = 5, TableIds = new List<int> { 5, 6 }, Capacity = 4 }
        };

        var stationTypes = DefaultDinerKitchenLayoutSeed.CreateStationTypes();
        var stations = DefaultDinerKitchenLayoutSeed.CreateStations(stationTypes);
        var heatLamp = DefaultDinerKitchenLayoutSeed.CreateHeatLamp();

        // Placed adjacent to cold_prep (presets.md) — cold_prep is StationId 3
        // at (2,0) in the kitchen layout seed.
        var freezer = new Freezer { FreezerId = 1, Position = new GridPosition(2, 1), Hitbox = new GridBounds(1, 1) };

        var bathrooms = new List<Bathroom>
        {
            new() { BathroomId = 1, Gender = BathroomGender.Male, Position = new GridPosition(10, 0), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1 },
            new() { BathroomId = 2, Gender = BathroomGender.Female, Position = new GridPosition(10, 1), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1 },
            new() { BathroomId = 3, Gender = BathroomGender.Unisex, Position = new GridPosition(10, 2), Hitbox = new GridBounds(1, 1), Capacity = 1, MaxConcurrentCleaners = 1 }
        };

        return new RestaurantLayout
        {
            Tables = tables,
            Clusters = clusters,
            KitchenStations = stations,
            HeatLamps = new List<HeatLamp> { heatLamp },
            Freezers = new List<Freezer> { freezer },
            Bathrooms = bathrooms
        };
    }

    private static Table CreateTable(int id, TableShape shape, GridPosition position, int clusterId)
    {
        var seats = shape.SeatDefinitions.Select(sd => new Seat
        {
            SeatIndex = sd.SeatIndex,
            RelativeOffset = sd.RelativeOffset,
            Orientation = sd.Orientation
        }).ToList();

        return new Table
        {
            TableId = id,
            Position = position,
            Hitbox = new GridBounds(1, 1),
            Shape = shape,
            Seats = seats,
            IsClean = true,
            ClusterId = clusterId
        };
    }
}
