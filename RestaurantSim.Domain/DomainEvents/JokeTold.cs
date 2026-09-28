using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record JokeTold(Guid RestaurantId, int ClusterId, int WaiterStaffId, int? TargetCustomerId, bool WasSuccessful, long Tick) : INotification;
