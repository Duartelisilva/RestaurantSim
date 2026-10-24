using MediatR;
using Microsoft.Extensions.Logging;
using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Commands.AdvanceCleaningProgress;
using RestaurantSim.Application.Commands.AdvanceDining;
using RestaurantSim.Application.Commands.AdvanceKitchen;
using RestaurantSim.Application.Commands.AdvanceMood;
using RestaurantSim.Application.Commands.AdvanceStaffShift;
using RestaurantSim.Application.Commands.EndDay;
using RestaurantSim.Application.Commands.ProcessQueueAbandonment;
using RestaurantSim.Application.Commands.SpawnArrival;
using RestaurantSim.Application.Commands.SpreadGroupMood;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Application.TickProcessing;

public sealed class RestaurantTickProcessor
{
    private readonly IRestaurantRepository _repository;
    private readonly ISender _sender;
    private readonly IAutoManagerRunner _autoManagerRunner;
    private readonly ILogger<RestaurantTickProcessor> _logger;

    public RestaurantTickProcessor(
        IRestaurantRepository repository,
        ISender sender,
        IAutoManagerRunner autoManagerRunner,
        ILogger<RestaurantTickProcessor> logger)
    {
        _repository = repository;
        _sender = sender;
        _autoManagerRunner = autoManagerRunner;
        _logger = logger;
    }

    public async Task AdvanceAllRestaurantsAsync(CancellationToken cancellationToken)
    {
        var restaurants = await _repository.GetAllAsync(cancellationToken);

        foreach (var restaurant in restaurants)
        {
            await AdvanceRestaurantAsync(restaurant, cancellationToken);
        }
    }

    // Advances ONE restaurant by exactly one tick (locked intra-tick order,
    // architecture doc §2a), then runs its AutoManager pass. The hosting loop
    // decides *when* a restaurant is due (per-restaurant TicksPerRealSecond).
    public async Task AdvanceRestaurantAsync(Restaurant restaurant, CancellationToken cancellationToken)
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
        await TryDispatchAsync(new AdvanceDiningCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        await TryDispatchAsync(new SpawnArrivalCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        await TryDispatchAsync(new AdvanceCleaningProgressCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        await TryDispatchAsync(new AdvanceMoodCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        await TryDispatchAsync(new AdvanceStaffShiftCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        await TryDispatchAsync(new SpreadGroupMoodCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        await TryDispatchAsync(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);
        await TryDispatchAsync(new EndDayCommand(restaurant.RestaurantId), restaurant.RestaurantId, cancellationToken);

        await TryRunAutoManagerAsync(restaurant.RestaurantId, cancellationToken);
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

    private async Task TryRunAutoManagerAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        try
        {
            await _autoManagerRunner.RunPassAsync(restaurantId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoManager pass threw for restaurant {RestaurantId}", restaurantId);
        }
    }
}
