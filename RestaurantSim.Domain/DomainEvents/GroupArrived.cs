using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record GroupArrived(Guid RestaurantId, int GroupId, long Tick) : INotification;
