using MediatR;
using Microsoft.Extensions.Logging;
using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.AutoManager;

// "Per-decision" lock acquisition (architecture doc §11) falls out naturally
// here — each ISender.Send call goes through the normal MediatR pipeline
// (RestaurantLockBehavior), acquiring and releasing the lock once per
// dispatched command. Nothing special has to be built for this; it's a
// consequence of dispatching one command per decision rather than batching.
public sealed class AutoManagerProcessor : IAutoManagerRunner
{
    private readonly IRestaurantRepository _repository;
    private readonly ISender _sender;
    private readonly ISeatingStrategy _seatingStrategy;
    private readonly ILogger<AutoManagerProcessor> _logger;

    public AutoManagerProcessor(
        IRestaurantRepository repository,
        ISender sender,
        ISeatingStrategy seatingStrategy,
        ILogger<AutoManagerProcessor> logger)
    {
        _repository = repository;
        _sender = sender;
        _seatingStrategy = seatingStrategy;
        _logger = logger;
    }

    public async Task RunPassAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(restaurantId, cancellationToken);
        if (restaurant is null) return;

        // AutoManagerSystem.Seating (entity doc §19's toggle set) — defaults
        // to on if the toggle is somehow absent (defensive).
        if (restaurant.AutoManagerToggles.GetValueOrDefault(AutoManagerSystem.Seating, true))
        {
            await RunSeatingPassAsync(restaurantId, cancellationToken);
        }

        // TaskPriority (Busser cleaning) and WaiterActions (jokes) passes are
        // added here once their underlying commands exist — neither is built
        // yet (see pending-doc-changes).
    }

    private async Task RunSeatingPassAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        // Loop: decide -> dispatch -> re-fetch fresh state -> decide again,
        // until the strategy finds nothing left to seat this pass. Re-fetching
        // each iteration matters — a prior decision in THIS pass changes the
        // queue/cluster availability the next decision must read.
        while (true)
        {
            var restaurant = await _repository.GetAsync(restaurantId, cancellationToken);
            if (restaurant is null) return;

            var decision = _seatingStrategy.DecideNextSeating(restaurant);
            if (decision is null) break;

            try
            {
                var result = await _sender.Send(new SeatGroupCommand(restaurantId, decision.Value.GroupId, decision.Value.ClusterId), cancellationToken);
                if (result.IsFailure)
                {
                    // Something changed between the strategy's decision and the
                    // command actually running (re-validated at handle-time,
                    // architecture doc §11) — stop this pass rather than retry
                    // indefinitely against a decision that's no longer valid.
                    _logger.LogWarning("AutoManager seating decision failed for restaurant {RestaurantId}: {Error}", restaurantId, result.Error);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoManager seating pass threw for restaurant {RestaurantId}", restaurantId);
                break;
            }
        }
    }
}
