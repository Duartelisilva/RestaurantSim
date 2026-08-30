using MediatR;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record OrderPlaced(
    Guid RestaurantId,
    int GroupId,
    int OrderId,
    ItemPhase Phase,
    IReadOnlyList<int> OrderItemIds,
    long Tick) : INotification;
