namespace RestaurantSim.Contracts.Events;

public sealed record GroupArrivedDto(int GroupId, int PartySize, long Tick);
