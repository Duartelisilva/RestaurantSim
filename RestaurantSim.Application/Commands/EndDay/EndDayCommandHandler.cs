using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Statistics;

namespace RestaurantSim.Application.Commands.EndDay;

public sealed class EndDayCommandHandler : IRequestHandler<EndDayCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly ISnapshotStore _snapshotStore;
    private readonly IPublisher _publisher;

    public EndDayCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        ISnapshotStore snapshotStore,
        IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _snapshotStore = snapshotStore;
        _publisher = publisher;
    }

    public async Task<Result> Handle(EndDayCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        if (restaurant.CustomerService.ActiveGroups.Count > 0 || restaurant.CustomerService.Queue.Count > 0)
            return Result.Success();

        var closing = ComputeClosingMinutes(config);
        var minutesIntoDay = MealPeriodCalculator.MinutesIntoDay(config.MinutesPerTick, restaurant.SimulationTick);

        var isPastClosing = closing >= 1440 ? minutesIntoDay == 0 : minutesIntoDay >= closing;
        if (!isPastClosing)
            return Result.Success();

        foreach (var bathroom in restaurant.Floor.Bathrooms)
        {
            bathroom.CleanlinessScore = 100f;
        }

        restaurant.CurrentDayNumber++;

        // Real snapshot now, using DailyStatsAccumulator's running totals
        // (fed by BillPaidStatsHandler/GroupLeftStatsHandler) plus the
        // per-tick TableUtilization sampling done in RestaurantTickProcessor.
        // AvgWaitTicks and Costs are left at 0 — deliberately, not stubbed to
        // look complete. No wait-time tracking or cost formulas exist
        // anywhere yet (see pending-doc-changes). ComplaintCount is 0
        // because ComplaintsBookModule is inactive by default (entity doc §11).
        var stats = restaurant.DailyStats;
        var snapshot = new StatisticsSnapshot(
            Tick: restaurant.SimulationTick,
            Revenue: stats.Revenue,
            CustomersServed: stats.CustomersServed,
            AvgWaitTicks: 0f,
            TableUtilization: stats.TableOccupancySampleCount > 0
                ? stats.TableOccupancySampleSum / stats.TableOccupancySampleCount
                : 0f,
            AvgFinalMood: stats.CustomersServed > 0
                ? stats.WeightedFinalMoodSum / stats.CustomersServed
                : 0f,
            ComplaintCount: 0,
            ActiveEventTypes: Array.Empty<string>(),
            TotalGameplayDays: restaurant.CurrentDayNumber,
            Costs: 0m,
            ReputationDelta: restaurant.Reputation - stats.ReputationAtDayStart);

        await _snapshotStore.AppendAsync(restaurant.RestaurantId, snapshot, cancellationToken);

        stats.Reset(reputationAtNewDayStart: restaurant.Reputation);

        var ticksPerDay = (long)Math.Round(1440.0 / config.MinutesPerTick);
        var currentDayStartTick = restaurant.SimulationTick / ticksPerDay * ticksPerDay;
        var openingMinutes = ComputeOpeningMinutes(config);
        var openingTicksIntoDay = (long)Math.Round(openingMinutes / (double)config.MinutesPerTick);

        restaurant.SimulationTick = currentDayStartTick + ticksPerDay + openingTicksIntoDay;

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(new DayEnded(restaurant.RestaurantId, restaurant.CurrentDayNumber, restaurant.SimulationTick), cancellationToken);

        return Result.Success();
    }

    private static int ComputeOpeningMinutes(RestaurantConfig config) =>
        MealPeriodCalculator.OpeningMinutes(config.ActiveMealPeriods);

    private static int ComputeClosingMinutes(RestaurantConfig config) =>
        config.ActiveMealPeriods.Select(MealPeriodCalculator.PeriodWindow).Max(w => w.EndMinutes);
}
