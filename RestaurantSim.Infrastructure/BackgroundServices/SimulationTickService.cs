using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestaurantSim.Application.TickProcessing;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Infrastructure.BackgroundServices;

// Thin hosting wrapper only — actual per-tick logic lives in
// RestaurantTickProcessor (Application layer), which is directly unit-testable.
//
// The loop wakes every LoopInterval and ticks each restaurant that is due,
// pacing each one by its own RestaurantConfig.TicksPerRealSecond (architecture
// doc §2). Practical limit: the wake-up granularity is ~10-15 ms (OS timer
// resolution), so rates above roughly 60 ticks/second will not be reached.
public sealed class SimulationTickService : BackgroundService
{
    private static readonly TimeSpan LoopInterval = TimeSpan.FromMilliseconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TickPacer _pacer = new();

    public SimulationTickService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clock = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(LoopInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRestaurantRepository>();
            var presets = scope.ServiceProvider.GetRequiredService<IPresetRepository<RestaurantConfig>>();
            var processor = scope.ServiceProvider.GetRequiredService<RestaurantTickProcessor>();

            foreach (var restaurant in await repository.GetAllAsync(stoppingToken))
            {
                // A restaurant whose config is missing has no rate to pace by; it
                // is skipped (the per-tick commands would fail on it anyway).
                var config = await presets.GetAsync(restaurant.ConfigId, stoppingToken);
                if (config is null)
                    continue;

                if (_pacer.ShouldTick(restaurant.RestaurantId, clock.Elapsed, config.TicksPerRealSecond))
                    await processor.AdvanceRestaurantAsync(restaurant, stoppingToken);
            }
        }
    }
}
