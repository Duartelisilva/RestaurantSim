using MediatR;
using RestaurantSim.Domain.CustomerService;

namespace RestaurantSim.Domain.DomainEvents;

// Past-tense fact: a seated group's DiningStage (DiningStageCalculator) changed. Carries
// the absolute resulting stage, never a transition, so a client sets state and a replayed
// event is harmless. NextVisit says what the next waiter visit is for ("choosing the main").
// Raised by AdvanceDiningCommandHandler; a just-seated group's first stage (Ordering) is
// implied by GroupSeated and has no event of its own.
public sealed record GroupDiningStageChanged(
    Guid RestaurantId,
    int GroupId,
    DiningStage Stage,
    DiningVisit NextVisit,
    long Tick) : INotification;
