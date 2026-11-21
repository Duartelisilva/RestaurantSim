using MediatR;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Domain.DomainEvents;

// Past-tense fact: a staff member's State or current task changed (clocked in/out, started or
// finished a task). The absolute resulting state, never a transition; Fatigue is the value at
// that moment — it is not pushed on its own every tick (architecture doc §3). Raised by
// StaffStateChangeBehavior after any restaurant command, so no handler has to remember to.
public sealed record StaffStateChanged(
    Guid RestaurantId,
    int StaffId,
    StaffState State,
    TaskType? TaskType,
    int? TaskTargetId,
    float Fatigue,
    long Tick) : INotification;
