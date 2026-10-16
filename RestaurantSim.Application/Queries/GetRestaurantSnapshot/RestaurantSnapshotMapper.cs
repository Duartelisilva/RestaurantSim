using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Application.Queries.GetRestaurantSnapshot;

internal static class RestaurantSnapshotMapper
{
    public static RestaurantSnapshotDto Map(Restaurant r)
    {
        var floor = r.Floor;
        var service = r.CustomerService;

        return new RestaurantSnapshotDto(
            r.RestaurantId,
            r.SimulationTick,
            r.CurrentDayNumber,
            r.Cash,
            r.Reputation,
            new FloorDto(
                floor.Tables.Select(t => new TableDto(
                    t.TableId, Point(t.Position), Size(t.Hitbox), t.ClusterId, t.IsClean)).ToList(),
                floor.Clusters.Select(c => new ClusterDto(
                    c.ClusterId,
                    c.TableIds.ToList(),
                    c.Capacity,
                    c.DeriveState(floor.Tables, service.Reservations, r.SimulationTick).ToString(),
                    c.OccupiedBy)).ToList(),
                floor.KitchenStations.Select(s => new StationDto(
                    s.StationId, s.StationType.StationTypeId, Point(s.Position), Size(s.Hitbox))).ToList(),
                floor.HeatLamps.Select(h => new FixtureDto(
                    h.HeatLampId, Point(h.Position), Size(h.Hitbox))).ToList(),
                floor.Freezers.Select(f => new FixtureDto(
                    f.FreezerId, Point(f.Position), Size(f.Hitbox))).ToList(),
                floor.Bathrooms.Select(b => new BathroomDto(
                    b.BathroomId, b.Gender.ToString(), Point(b.Position), Size(b.Hitbox),
                    b.CleanlinessScore)).ToList()),
            r.StaffRoster.Staff.Select(s => new StaffDto(
                s.StaffId, s.Name, s.Role.ToString(), s.State.ToString())).ToList(),
            service.Queue.Select(g => new QueuedGroupDto(g.GroupId, g.PartySize)).ToList(),
            service.ActiveGroups.Select(g => new ActiveGroupDto(
                g.GroupId, g.PartySize, g.ClusterId,
                g.LifecycleState.ToString(), g.CoursePhase.ToString())).ToList());
    }

    private static GridPointDto Point(GridPosition p) => new(p.X, p.Y);

    private static GridSizeDto Size(GridBounds b) => new(b.Width, b.Height);
}
