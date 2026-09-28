using MediatR;
using Microsoft.Extensions.Logging;
using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Commands.CleanBathroom;
using RestaurantSim.Application.Commands.CleanTable;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Commands.TellJoke;
using RestaurantSim.Application.Common;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.AutoManager.Strategies.TaskPriority;
using RestaurantSim.AutoManager.Strategies.WaiterAction;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.AutoManager;

public sealed class AutoManagerProcessor : IAutoManagerRunner
{
    private const float DefaultDirtyThreshold = 30f;
    private const float DefaultAlmostDirtyThreshold = 60f;
    private const float DefaultAutoJokeCooldownSeconds = 60f;
    private const float DefaultMoodGapThreshold = 20f;

    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly ISender _sender;
    private readonly ISeatingStrategy _seatingStrategy;
    private readonly IStaffTaskPriorityStrategy _taskPriorityStrategy;
    private readonly IWaiterActionStrategy _waiterActionStrategy;
    private readonly ILogger<AutoManagerProcessor> _logger;

    public AutoManagerProcessor(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        ISender sender,
        ISeatingStrategy seatingStrategy,
        IStaffTaskPriorityStrategy taskPriorityStrategy,
        IWaiterActionStrategy waiterActionStrategy,
        ILogger<AutoManagerProcessor> logger)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _sender = sender;
        _seatingStrategy = seatingStrategy;
        _taskPriorityStrategy = taskPriorityStrategy;
        _waiterActionStrategy = waiterActionStrategy;
        _logger = logger;
    }

    public async Task RunPassAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(restaurantId, cancellationToken);
        if (restaurant is null) return;

        if (restaurant.AutoManagerToggles.GetValueOrDefault(AutoManagerSystem.Seating, true))
        {
            await RunSeatingPassAsync(restaurantId, cancellationToken);
        }

        if (restaurant.AutoManagerToggles.GetValueOrDefault(AutoManagerSystem.TaskPriority, true))
        {
            await RunTaskPriorityPassAsync(restaurantId, cancellationToken);
        }

        if (restaurant.AutoManagerToggles.GetValueOrDefault(AutoManagerSystem.WaiterActions, true))
        {
            await RunWaiterActionsPassAsync(restaurantId, cancellationToken);
        }
    }

    private async Task RunSeatingPassAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
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

    private async Task RunTaskPriorityPassAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var restaurantForConfig = await _repository.GetAsync(restaurantId, cancellationToken);
        if (restaurantForConfig is null) return;

        var config = await _presetRepository.GetAsync(restaurantForConfig.ConfigId, cancellationToken);
        if (config is null) return;

        var dirtyThreshold = config.DirtyThreshold ?? DefaultDirtyThreshold;
        var almostDirtyThreshold = config.AlmostDirtyThreshold ?? DefaultAlmostDirtyThreshold;

        var idleBusserIds = restaurantForConfig.StaffRoster.Staff
            .Where(s => s.Role == StaffRole.Busser && s.State == StaffState.Idle)
            .Select(s => s.StaffId)
            .ToList();

        foreach (var busserId in idleBusserIds)
        {
            var current = await _repository.GetAsync(restaurantId, cancellationToken);
            if (current is null) return;

            var busser = current.StaffRoster.Staff.FirstOrDefault(s => s.StaffId == busserId);
            if (busser is null || busser.State != StaffState.Idle) continue;

            var decision = _taskPriorityStrategy.DecideNextBusserTask(current, dirtyThreshold, almostDirtyThreshold);
            if (decision is null) continue;

            IRequest<Result> command = decision.Value.TaskType == TaskType.CleanTable
                ? new CleanTableCommand(restaurantId, busserId, decision.Value.TargetId)
                : new BussersCleanBathroomCommand(restaurantId, busserId, decision.Value.TargetId);

            try
            {
                var result = await _sender.Send(command, cancellationToken);
                if (result.IsFailure)
                    _logger.LogWarning("AutoManager task-priority decision failed for restaurant {RestaurantId}: {Error}", restaurantId, result.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoManager task-priority pass threw for restaurant {RestaurantId}", restaurantId);
            }
        }
    }

    private async Task RunWaiterActionsPassAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(restaurantId, cancellationToken);
        if (restaurant is null) return;

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null) return;

        var cooldownTicks = (config.AutoJokeCooldownSeconds ?? DefaultAutoJokeCooldownSeconds) * config.TicksPerRealSecond;
        var moodGapThreshold = config.MoodGapThreshold ?? DefaultMoodGapThreshold;

        var decision = _waiterActionStrategy.DecideNextJoke(restaurant, cooldownTicks, moodGapThreshold);
        if (decision is null) return;

        try
        {
            var result = await _sender.Send(new TellJokeCommand(restaurantId, decision.Value.ClusterId, decision.Value.TargetCustomerId), cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("AutoManager waiter-action decision failed for restaurant {RestaurantId}: {Error}", restaurantId, result.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoManager waiter-action pass threw for restaurant {RestaurantId}", restaurantId);
        }
    }
}
