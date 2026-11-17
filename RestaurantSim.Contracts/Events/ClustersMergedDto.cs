using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Contracts.Events;

// ClusterId survives (the lowest merged id); every id in MergedClusterIds no longer exists
// afterwards except ClusterId itself, which now owns all of TableIds. The resulting topology,
// not an instruction, so applying it twice is harmless; Tick as in GroupSeatedDto.
// Moves slide the tables together (empty when they already touched). The cluster is Moving
// for DurationTicks (a real-time length of DurationTicks / TicksPerRealSecond); a
// TableMoveCompletedDto then says it is usable again.
public sealed record ClustersMergedDto(
    int ClusterId,
    IReadOnlyList<int> MergedClusterIds,
    IReadOnlyList<int> TableIds,
    int Capacity,
    IReadOnlyList<TableMoveDto> Moves,
    long DurationTicks,
    long Tick);
