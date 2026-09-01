using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Named in CLAUDE.md's DomainEvents/ list already. Fires whether the item
// went straight to Ready or was released from HeldUnderLamp — the SignalR
// translation layer doesn't need to distinguish, both mean "food is ready."
public sealed record OrderItemReady(Guid RestaurantId, int OrderItemId, int GroupId, bool IsHighQuality, long Tick) : INotification;
