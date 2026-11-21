using MediatR;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Common;

// Publishes StaffStateChanged for every staff member whose State or current task differs
// before and after a restaurant command. One place for every command (tick commands,
// AutoManager decisions, player commands, whatever staff-touching commands come later), so
// no handler has to remember to raise the event and none can forget.
//
// Registered AFTER RestaurantLockBehavior and ValidationBehavior: it runs inside the
// per-restaurant lock, around the handler only, and after the handler's own save, so a
// listener never sees unsaved state (architecture doc §11).
public sealed class StaffStateChangeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IRestaurantRepository _repository;
    private readonly IPublisher _publisher;

    public StaffStateChangeBehavior(IRestaurantRepository repository, IPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IRestaurantScopedRequest scoped)
            return await next();

        var before = await _repository.GetAsync(scoped.RestaurantId, cancellationToken) is { } existing
            ? existing.StaffRoster.Staff.ToDictionary(s => s.StaffId, StaffSnapshot.Of)
            : new Dictionary<int, StaffSnapshot>();

        var response = await next();

        var restaurant = await _repository.GetAsync(scoped.RestaurantId, cancellationToken);
        if (restaurant is null)
            return response;

        foreach (var staff in restaurant.StaffRoster.Staff)
        {
            if (before.TryGetValue(staff.StaffId, out var was) && was == StaffSnapshot.Of(staff))
                continue;

            await _publisher.Publish(
                new StaffStateChanged(
                    restaurant.RestaurantId, staff.StaffId, staff.State, staff.CurrentTaskType,
                    staff.CurrentTaskTargetId, staff.Stats.Get(StatType.Fatigue), restaurant.SimulationTick),
                cancellationToken);
        }

        return response;
    }

    // What counts as "changed": State and the current task. Fatigue moves every tick and
    // would drown everything; it rides along on the event when something else changes.
    private readonly record struct StaffSnapshot(StaffState State, TaskType? TaskType, int? TaskTargetId)
    {
        public static StaffSnapshot Of(StaffMember staff) =>
            new(staff.State, staff.CurrentTaskType, staff.CurrentTaskTargetId);
    }
}
