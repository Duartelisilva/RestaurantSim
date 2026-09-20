using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record TableCleaned(Guid RestaurantId, int TableId, long Tick) : INotification;
