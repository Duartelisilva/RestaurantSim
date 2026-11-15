using System.Numerics;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Tests.Support;

public static class TestFloors
{
    // One standalone 2-top (N/S seats) per x in row 0, table n in cluster n, tick 100. The
    // floor may push tables together across `maxMergeGapCells` empty cells (0 = touching only).
    public static Restaurant Row(int maxMergeGapCells, params int[] xs)
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
            Floor = new FloorState { MaxMergeGapCells = maxMergeGapCells },
            StaffRoster = new StaffRosterState(),
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState()
        };

        for (var i = 0; i < xs.Length; i++)
        {
            var id = i + 1;
            restaurant.Floor.Tables.Add(new Table
            {
                TableId = id,
                Position = new GridPosition(xs[i], 0),
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
