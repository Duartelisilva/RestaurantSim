using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Application.Commands.EndDay;

public sealed class EndDayCommandHandler : IRequestHandler<EndDayCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IPublisher _publisher;

    public EndDayCommandHandler(IRestaurantRepository repository, IPresetRepository<RestaurantConfig> presetRepository, IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
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

        // Not eligible — routine no-op, not a failure (mechanics doc §15's
        // DayEndEligible check).
        if (restaurant.CustomerService.ActiveGroups.Count > 0 || restaurant.CustomerService.Queue.Count > 0)
            return Result.Success();

        var closing = ComputeClosingMinutes(config);
        var minutesIntoDay = MealPeriodCalculator.MinutesIntoDay(config.MinutesPerTick, restaurant.SimulationTick);

        // Edge case, flagged: Closing=1440 (midnight) can never satisfy
        // "minutesIntoDay >= Closing" literally, since minutesIntoDay is a
        // modulo result always in [0,1440) — it wraps to 0, never reaches
        // 1440. Special-cased here as "check minutesIntoDay == 0" instead.
        // Holds for MVP's MinutesPerTick=1 (minutesIntoDay passes through
        // exactly 0 once per day); not a general solution for every possible
        // tick-resolution configuration.
        var isPastClosing = closing >= 1440 ? minutesIntoDay == 0 : minutesIntoDay >= closing;
        if (!isPastClosing)
            return Result.Success();

        // Mechanics doc §15 actions 1-2 (StatisticsSnapshot append, day-end
        // costs) are DELIBERATELY NOT implemented — no accumulation system
        // exists anywhere to track Revenue/CustomersServed/wages/etc. as
        // running totals. Appending a snapshot of fake zeros would be worse
        // than honestly skipping it. See pending-doc-changes.

        foreach (var bathroom in restaurant.Floor.Bathrooms)
        {
            bathroom.CleanlinessScore = 100f;
        }

        restaurant.CurrentDayNumber++;

        var ticksPerDay = (long)Math.Round(1440.0 / config.MinutesPerTick);
        var currentDayStartTick = restaurant.SimulationTick / ticksPerDay * ticksPerDay;
        var openingMinutes = ComputeOpeningMinutes(config);
        var openingTicksIntoDay = (long)Math.Round(openingMinutes / (double)config.MinutesPerTick);

        // Auto-advances directly to next day's Opening — nothing happens
        // overnight (no arrivals, no staff clocked in), so skipping straight
        // there is safe by construction (mechanics doc §15).
        restaurant.SimulationTick = currentDayStartTick + ticksPerDay + openingTicksIntoDay;

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(new DayEnded(restaurant.RestaurantId, restaurant.CurrentDayNumber, restaurant.SimulationTick), cancellationToken);

        return Result.Success();
    }

    private static int ComputeOpeningMinutes(RestaurantConfig config) =>
        config.ActiveMealPeriods.Select(MealPeriodCalculator.PeriodWindow).Min(w => w.StartMinutes);

    private static int ComputeClosingMinutes(RestaurantConfig config) =>
        config.ActiveMealPeriods.Select(MealPeriodCalculator.PeriodWindow).Max(w => w.EndMinutes);
}
