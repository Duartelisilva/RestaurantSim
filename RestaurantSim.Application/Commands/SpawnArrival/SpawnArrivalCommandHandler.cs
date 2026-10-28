using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Application.Commands.SpawnArrival;

public sealed class SpawnArrivalCommandHandler : IRequestHandler<SpawnArrivalCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IPublisher _publisher;
    private readonly ArrivalGroupGenerator _groupGenerator;

    public SpawnArrivalCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IPublisher publisher,
        ArrivalGroupGenerator groupGenerator)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _publisher = publisher;
        _groupGenerator = groupGenerator;
    }

    public async Task<Result> Handle(SpawnArrivalCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        if (restaurant.SimulationTick < restaurant.CustomerService.NextArrivalTick)
            return Result.Success();

        // Now uses the shared MealPeriodCalculator instead of a private copy —
        // Ordering needs the same lookup, so this was extracted to avoid a
        // second duplicate implementation drifting out of sync.
        var activePeriod = MealPeriodCalculator.FindActive(config.ActiveMealPeriods, config.MinutesPerTick, restaurant.SimulationTick);
        if (activePeriod is null)
        {
            restaurant.CustomerService.NextArrivalTick = restaurant.SimulationTick + 1;
            await _repository.SaveAsync(restaurant, cancellationToken);
            return Result.Success();
        }

        if (restaurant.CustomerService.Queue.Count >= config.MaxQueueLength)
        {
            restaurant.Reputation = MathF.Max(0, restaurant.Reputation - restaurant.Reputation * 0.01f);
            restaurant.CustomerService.NextArrivalTick =
                restaurant.SimulationTick + ComputeIntervalTicks(config, restaurant, activePeriod);

            await _repository.SaveAsync(restaurant, cancellationToken);
            await _publisher.Publish(new GroupTurnedAway(restaurant.RestaurantId, restaurant.Reputation, restaurant.SimulationTick), cancellationToken); return Result.Success();
        }

        var groupId = restaurant.CustomerService.NextGroupId++;
        var group = _groupGenerator.Generate(
            groupId,
            restaurant.SimulationTick,
            nextCustomerId: () => restaurant.CustomerService.NextCustomerId++);

        // A party larger than anything the layout could EVER seat (even by merging tables) is
        // turned away at the door instead of clogging the queue. The reputation penalty is a
        // config fraction of current reputation, 0 by default. A layout with no tables at all
        // is degenerate and has no limit to apply.
        var maxPartySize = TableMergeRules.MaxPartySize(restaurant.Floor.Tables);
        if (maxPartySize > 0 && group.PartySize > maxPartySize)
        {
            restaurant.Reputation = MathF.Max(0, restaurant.Reputation - restaurant.Reputation * config.OversizedPartyReputationPenalty);
            restaurant.CustomerService.NextArrivalTick =
                restaurant.SimulationTick + ComputeIntervalTicks(config, restaurant, activePeriod);

            await _repository.SaveAsync(restaurant, cancellationToken);
            await _publisher.Publish(
                new GroupTurnedAway(restaurant.RestaurantId, restaurant.Reputation, restaurant.SimulationTick, TurnedAwayReason.PartyTooLarge),
                cancellationToken);

            return Result.Success();
        }

        restaurant.CustomerService.Queue.Add(group);

        restaurant.CustomerService.NextArrivalTick =
            restaurant.SimulationTick + ComputeIntervalTicks(config, restaurant, activePeriod);

        await _repository.SaveAsync(restaurant, cancellationToken);
        await _publisher.Publish(new GroupArrived(restaurant.RestaurantId, group.GroupId, group.PartySize, restaurant.SimulationTick), cancellationToken);

        return Result.Success();
    }

    private static int ComputeIntervalTicks(RestaurantConfig config, Restaurant restaurant, MealPeriodDefinition period)
    {
        var reputationModifier = 0.5 + restaurant.Reputation / 100.0;

        var queuePressureModifier = Math.Clamp(
            1.0 - restaurant.CustomerService.Queue.Count / (double)config.MaxQueueLength * 0.7, 0.3, 1.0);

        var timeOfDayModifier = ComputeTimeOfDayModifier(config, restaurant.SimulationTick, period);

        var baseIntervalTicks = config.BaseArrivalIntervalSeconds * config.TicksPerRealSecond;
        var intervalTicks = baseIntervalTicks / (reputationModifier * queuePressureModifier * timeOfDayModifier);

        return Math.Max(1, (int)Math.Round(intervalTicks));
    }

    private static double ComputeTimeOfDayModifier(RestaurantConfig config, long currentTick, MealPeriodDefinition period)
    {
        var minutesIntoDay = MealPeriodCalculator.MinutesIntoDay(config.MinutesPerTick, currentTick);
        var (startMinutes, endMinutes) = MealPeriodCalculator.PeriodWindow(period);
        var periodDuration = endMinutes - startMinutes;
        if (periodDuration <= 0) return 1.0;

        var fraction = Math.Clamp((minutesIntoDay - startMinutes) / (double)periodDuration, 0, 1);
        return 0.7 + 0.8 * Math.Sin(Math.PI * fraction);
    }
}
