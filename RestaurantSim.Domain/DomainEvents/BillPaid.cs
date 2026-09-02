using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// TipAmount is carried here rather than mutated into a StaffMember's Morale
// stat directly — no tip-to-Morale conversion formula exists anywhere in the
// locked docs (entity doc §7 flags Morale's formula as "not yet defined").
// A future Morale system subscribes to this event once that formula exists.
public sealed record BillPaid(
    Guid RestaurantId,
    int GroupId,
    int OrderId,
    decimal Revenue,
    decimal TipAmount,
    long Tick) : INotification;
