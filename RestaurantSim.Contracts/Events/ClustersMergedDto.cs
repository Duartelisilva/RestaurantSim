namespace RestaurantSim.Contracts.Events;

// ClusterId survives (the lowest merged id); every id in MergedClusterIds no longer exists
// afterwards except ClusterId itself, which now owns all of TableIds. The resulting topology,
// not an instruction, so applying it twice is harmless; Tick as in GroupSeatedDto.
public sealed record ClustersMergedDto(
    int ClusterId,
    IReadOnlyList<int> MergedClusterIds,
    IReadOnlyList<int> TableIds,
    int Capacity,
    long Tick);
