using MediatR;
using Microsoft.Extensions.Logging;
using RestaurantSim.Application.Commands.AdvanceKitchen;
using RestaurantSim.Application.Commands.AdvanceMood;
using RestaurantSim.Application.Commands.AdvanceStaffShift;
using RestaurantSim.Application.Commands.ProcessQueueAbandonment;
using RestaurantSim.Application.Commands.SpawnArrival;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Application.TickProcessing;

// Dispatches the locked intra-tick execution order (architecture doc §2a) —
// as much of it as exists yet:
//   Step 1 (resolve scheduled completions): AdvanceKitchen, SpawnArrival
//   Step 2 (continuous per-tick accumulation): AdvanceMood, AdvanceStaffShift
//   Step 4 (deterministic threshold checks): ProcessQueueAbandonment
// Step 3 (intra-group Mood spread) and step 5 (day/meal-period boundaries)
// aren't built yet — they slot into this same sequence once they exist.
public sealed class RestaurantTickProcessor
{
    private readonly IRestaurantRepository _repository;
    private readonly ISender _sender;
    private readonly ILogger<RestaurantTickProcessor> _logger;

    public RestaurantTickProcessor(IRestaurantRepository repository, ISender sender, ILogger<RestaurantTickProcessor> logger)
    {
        _repository = repository;
        _sender = sender;
        _logger = logger;
    }

    public async Task AdvanceAllRestaurantsAsync(CancellationToken cancellationToken)
    {
        var restaurants = await _repository.GetAllAsync(cancellationToken);

        foreach (var restaurant in restaurants)
        {
            restaurant.SimulationTick++;
            await _repository.SaveAsync(restaurant, cancellationToken);

            await TryDispatchAsync(new AdvanceKitchenCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new SpawnArrivalCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new AdvanceMoodCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new AdvanceStaffShiftCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        }
    }

    private async Task TryDispatchAsync<TCommand>(TCommand command, Guid restaurantId, CancellationToken cancellationToken)
        where TCommand : IRequest<Result>
    {
        try
        {
            var result = await _sender.Send(command, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Tick command {Command} failed for restaurant {RestaurantId}: {Error}", typeof(TCommand).Name, restaurantId, result.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tick command {Command} threw for restaurant {RestaurantId}", typeof(TCommand).Name, restaurantId);
        }
    }
}
