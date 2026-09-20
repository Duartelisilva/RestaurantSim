using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record BathroomCleaned(Guid RestaurantId, int BathroomId, long Tick) : INotification;
