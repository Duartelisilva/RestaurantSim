using MediatR;
using Microsoft.Extensions.Logging;
using RestaurantSim.Application.AutoManager;
using RestaurantSim.Application.Commands.CleanBathroom;
using RestaurantSim.Application.Commands.CleanTable;
using RestaurantSim.Application.Commands.SeatGroup;
using RestaurantSim.Application.Common;
using RestaurantSim.AutoManager.Strategies.Seating;
using RestaurantSim.AutoManager.Strategies.TaskPriority;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.AutoManager;

public sealed class AutoManagerProcessor : IAutoManagerRunner
{
    // No doc-cited example values exist for either (mechanics doc §12's
    // two-tier thresholds) — first-pass proposed defaults.
    private const float DefaultDirtyThreshold = 30f;
    private const float DefaultAlmostDirtyThreshold = 60f;

    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly ISender _sender;
    private readonly ISeatingStrategy _seatingStrategy;
    private readonly IStaffTaskPriorityStrategy _taskPriorityStrategy;
    private readonly ILogger<AutoManagerProcessor> _logger;

    public AutoManagerProcessor(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        ISender sender,
        ISeatingStrategy seatingStrategy,
        IStaffTaskPriorityStrategy taskPriorityStrategy,
        ILogger<AutoManagerProcessor> logger)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _sender = sender;
        _seatingStrategy = seatingStrategy;
        _taskPriorityStrategy = taskPriorityStrategy;
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

        // WaiterActions pass added once its underlying commands exist (see
        // pending-doc-changes) — jokes/soothing/upsell/check-in aren't built.
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
        var config = await _presetRepository.GetAsync((await _repository.GetAsync(restaurantId, cancellationToken))?.ConfigId ?? "", cancellationToken);
        if (config is null) return;

        var dirtyThreshold = config.DirtyThreshold ?? DefaultDirtyThreshold;
        var almostDirtyThreshold = config.AlmostDirtyThreshold ?? DefaultAlmostDirtyThreshold;

        var restaurant = await _repository.GetAsync(restaurantId, cancellationToken);
        if (restaurant is null) return;

        // One decision per currently-idle Busser this pass — a Busy Busser is
        // never asked (tier 1, "finish current task," is this omission, not
        // a strategy branch).
        var idleBusserIds = restaurant.StaffRoster.Staff
            .Where(s => s.Role == StaffRole.Busser && s.State == StaffState.Idle)
            .Select(s => s.StaffId)
            .ToList();

        foreach (var busserId in idleBusserIds)
        {
            var current = await _repository.GetAsync(restaurantId, cancellationToken);
            if (current is null) return;

            var busser = current.StaffRoster.Staff.FirstOrDefault(s => s.StaffId == busserId);
            if (busser is null || busser.State != StaffState.Idle) continue; // already picked up something this pass

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
}
