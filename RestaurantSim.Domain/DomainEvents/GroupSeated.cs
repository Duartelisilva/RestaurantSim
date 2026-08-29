using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Past-tense fact (CLAUDE.md naming convention). Domain references MediatR only
// for the INotification marker interface — a judgment call, not stated outright
// in the architecture doc; flagged for confirmation.
public sealed record GroupSeated(Guid RestaurantId, int GroupId, int ClusterId, long Tick) : INotification;
