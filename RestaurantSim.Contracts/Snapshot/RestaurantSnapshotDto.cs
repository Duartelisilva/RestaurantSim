namespace RestaurantSim.Contracts.Snapshot;

public sealed record GridPointDto(int X, int Y);

public sealed record GridSizeDto(int Width, int Height);

public sealed record TableDto(
    int TableId,
    GridPointDto Position,
    GridSizeDto Size,
    int? ClusterId,
    bool IsClean);

public sealed record ClusterDto(
    int ClusterId,
    IReadOnlyList<int> TableIds,
    int Capacity,
    string State,
    int? OccupiedByGroupId);

public sealed record StationDto(
    int StationId,
    string StationTypeId,
    GridPointDto Position,
    GridSizeDto Size);

public sealed record FixtureDto(
    int Id,
    GridPointDto Position,
    GridSizeDto Size);

public sealed record BathroomDto(
    int BathroomId,
    string Gender,
    GridPointDto Position,
    GridSizeDto Size,
    float CleanlinessScore);

public sealed record FloorDto(
    IReadOnlyList<TableDto> Tables,
    IReadOnlyList<ClusterDto> Clusters,
    IReadOnlyList<StationDto> Stations,
    IReadOnlyList<FixtureDto> HeatLamps,
    IReadOnlyList<FixtureDto> Freezers,
    IReadOnlyList<BathroomDto> Bathrooms);

public sealed record StaffDto(
    int StaffId,
    string Name,
    string Role,
    string State);

public sealed record QueuedGroupDto(
    int GroupId,
    int PartySize);

public sealed record ActiveGroupDto(
    int GroupId,
    int PartySize,
    int? ClusterId,
    string LifecycleState,
    string CoursePhase);

public sealed record RestaurantSnapshotDto(
    Guid RestaurantId,
    long SimulationTick,
    int CurrentDayNumber,
    decimal Cash,
    float Reputation,
    FloorDto Floor,
    IReadOnlyList<StaffDto> Staff,
    IReadOnlyList<QueuedGroupDto> Queue,
    IReadOnlyList<ActiveGroupDto> ActiveGroups);
