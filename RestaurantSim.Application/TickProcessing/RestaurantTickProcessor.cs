using MediatR;
using Microsoft.Extensions.Logging;
using RestaurantSim.Application.Commands.AdvanceKitchen;
using RestaurantSim.Application.Commands.AdvanceMood;
using RestaurantSim.Application.Commands.AdvanceStaffShift;
using RestaurantSim.Application.Commands.EndDay;
using RestaurantSim.Application.Commands.ProcessQueueAbandonment;
using RestaurantSim.Application.Commands.SpawnArrival;
using RestaurantSim.Application.Commands.SpreadGroupMood;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Application.TickProcessing;

// Dispatches the FULL locked intra-tick execution order (architecture doc
// §2a) — every step now has an implementation:
//   Step 1 (resolve scheduled completions): AdvanceKitchen, SpawnArrival
//   Step 2 (continuous per-tick accumulation): AdvanceMood, AdvanceStaffShift
//   Step 3 (intra-group Mood spread): SpreadGroupMood
//   Step 4 (deterministic threshold checks): ProcessQueueAbandonment
//   Step 5 (day/meal-period boundary check): EndDayCommand
// Table-occupancy sampling (feeds StatisticsSnapshot.TableUtilization) is
// done inline here, same treatment as the SimulationTick++ increment right
// above it — simple aggregate bookkeeping, not business logic warranting a
// full dispatched command.
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

            if (restaurant.Floor.Clusters.Count > 0)
            {
                var occupiedFraction = restaurant.Floor.Clusters.Count(c => c.OccupiedBy != null) / (float)restaurant.Floor.Clusters.Count;
                restaurant.DailyStats.TableOccupancySampleSum += occupiedFraction;
                restaurant.DailyStats.TableOccupancySampleCount++;
            }

            await _repository.SaveAsync(restaurant, cancellationToken);

            await TryDispatchAsync(new AdvanceKitchenCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new SpawnArrivalCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new AdvanceMoodCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new AdvanceStaffShiftCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new SpreadGroupMoodCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
            await TryDispatchAsync(new EndDayCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
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
