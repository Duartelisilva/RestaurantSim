namespace RestaurantSim.Contracts.Events;

// Deliberately omits the Domain event's PartySize and FinalAverageMood —
// those feed statistics, not the floor view.
public sealed record GroupLeftDto(int GroupId, int ClusterId, long Tick);
