using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Staff;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Application.Commands.AdvanceCleaningProgress;

public sealed class AdvanceCleaningProgressCommandHandler : IRequestHandler<AdvanceCleaningProgressCommand, Result>
{
    private const float DefaultTableCleaningBaseSeconds = 60f;
    private const float DefaultBathroomCleaningBaseSeconds = 180f;

    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IPublisher _publisher;

    public AdvanceCleaningProgressCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(AdvanceCleaningProgressCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var completedTableEvents = new List<TableCleaned>();
        var completedBathroomEvents = new List<BathroomCleaned>();

        var activeBussers = restaurant.StaffRoster.Staff
            .Where(s => s.Role == StaffRole.Busser && s.State == StaffState.Busy)
            .ToList();

        foreach (var busser in activeBussers)
        {
            var speedMultiplier = PerformanceMultipliers.SpeedMultiplier(busser.Stats.Get(StatType.Speed));
            var fatigueMultiplier = PerformanceMultipliers.FatigueMultiplier(busser.Stats.Get(StatType.Fatigue));

            if (busser.CurrentTaskType == TaskType.CleanTable && busser.CurrentTaskTargetId is int tableId)
            {
                var table = restaurant.Floor.Tables.FirstOrDefault(t => t.TableId == tableId);
                if (table is null) { FreeBusser(busser); continue; }

                var baseSeconds = config.TableCleaningBaseSeconds ?? DefaultTableCleaningBaseSeconds;
                var baseTicks = KitchenPrepTimeCalculator.PrepTicks(baseSeconds, config.TicksPerRealSecond);
                var effectiveTicks = baseTicks * speedMultiplier * fatigueMultiplier;
                var progressPerTick = 100f / effectiveTicks;

                table.CleaningProgress += progressPerTick;
                if (table.CleaningProgress >= 100f)
                {
                    table.IsClean = true;
                    table.CleaningProgress = 0f;
                    FreeBusser(busser);
                    completedTableEvents.Add(new TableCleaned(restaurant.RestaurantId, tableId, restaurant.SimulationTick));

                    // Joke fatigue resets when the cluster fully clears
                    // (NeedsCleaning -> Free), mechanics doc §11 — only once
                    // EVERY member table is clean, not just this one.
                    var parentCluster = restaurant.Floor.Clusters.FirstOrDefault(c => c.TableIds.Contains(tableId));
                    if (parentCluster is not null &&
                        parentCluster.DeriveState(restaurant.Floor.Tables, restaurant.CustomerService.Reservations, restaurant.SimulationTick) == TableClusterState.Free)
                    {
                        parentCluster.RecentJokeCount = 0;
                    }
                }
            }
            else if (busser.CurrentTaskType == TaskType.CleanBathroom && busser.CurrentTaskTargetId is int bathroomId)
            {
                var bathroom = restaurant.Floor.Bathrooms.FirstOrDefault(b => b.BathroomId == bathroomId);
                if (bathroom is null) { FreeBusser(busser); continue; }

                var baseSeconds = config.BathroomCleaningBaseSeconds ?? DefaultBathroomCleaningBaseSeconds;
                var baseTicks = KitchenPrepTimeCalculator.PrepTicks(baseSeconds, config.TicksPerRealSecond);
                var effectiveTicks = baseTicks * speedMultiplier * fatigueMultiplier;
                var progressPerTick = 100f / effectiveTicks;

                bathroom.CleanlinessScore = Math.Clamp(bathroom.CleanlinessScore + progressPerTick, 0, 100);
                if (bathroom.CleanlinessScore >= 100f)
                {
                    FreeBusser(busser);
                    completedBathroomEvents.Add(new BathroomCleaned(restaurant.RestaurantId, bathroomId, restaurant.SimulationTick));
                }
            }
        }

        if (completedTableEvents.Count == 0 && completedBathroomEvents.Count == 0)
            return Result.Success();

        await _repository.SaveAsync(restaurant, cancellationToken);

        foreach (var evt in completedTableEvents) await _publisher.Publish(evt, cancellationToken);
        foreach (var evt in completedBathroomEvents) await _publisher.Publish(evt, cancellationToken);

        return Result.Success();
    }

    private static void FreeBusser(StaffMember busser)
    {
        busser.State = StaffState.Idle;
        busser.CurrentTaskType = null;
        busser.CurrentTaskTargetId = null;
    }
}
