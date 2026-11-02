namespace RestaurantSim.Contracts.Events;

// Wire contract for a seated group's dining stage. Stage/NextVisit use the names in
// Contracts.Snapshot.DiningStageNames / DiningVisitNames. The absolute resulting stage,
// never a transition, so applying it twice is harmless; Tick as in GroupSeatedDto.
public sealed record GroupDiningStageChangedDto(int GroupId, string Stage, string NextVisit, long Tick);
