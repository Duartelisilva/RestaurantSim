using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Application.Commands.SkipToNextMealPeriod;

public sealed class SkipToNextMealPeriodCommandHandler : IRequestHandler<SkipToNextMealPeriodCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;

    public SkipToNextMealPeriodCommandHandler(IRestaurantRepository repository, IPresetRepository<RestaurantConfig> presetRepository)
    {
        _repository = repository;
        _presetRepository = presetRepository;
    }

    public async Task<Result> Handle(SkipToNextMealPeriodCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        if (restaurant.CustomerService.ActiveGroups.Count > 0 || restaurant.CustomerService.Queue.Count > 0)
            return Result.Failure("Cannot skip while customers are present.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var minutesIntoDay = MealPeriodCalculator.MinutesIntoDay(config.MinutesPerTick, restaurant.SimulationTick);
        var windows = config.ActiveMealPeriods.Select(MealPeriodCalculator.PeriodWindow).OrderBy(w => w.StartMinutes).ToList();

        for (var i = 0; i < windows.Count - 1; i++)
        {
            var isInGap = minutesIntoDay >= windows[i].EndMinutes && minutesIntoDay < windows[i + 1].StartMinutes;
            if (!isInGap) continue;

            var ticksPerDay = (long)Math.Round(1440.0 / config.MinutesPerTick);
            var currentDayStartTick = restaurant.SimulationTick / ticksPerDay * ticksPerDay;
            var nextStartTicksIntoDay = (long)Math.Round(windows[i + 1].StartMinutes / (double)config.MinutesPerTick);

            restaurant.SimulationTick = currentDayStartTick + nextStartTicksIntoDay;
            await _repository.SaveAsync(restaurant, cancellationToken);
            return Result.Success();
        }

        return Result.Failure("Not currently in a gap between two active meal periods.");
    }
}
