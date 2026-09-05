using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestaurantSim.Application.TickProcessing;

namespace RestaurantSim.Infrastructure.BackgroundServices;

// First-pass simplification, flagged: architecture doc §2 says the loop
// interval derives from each restaurant's own TicksPerRealSecond. MVP only
// ever has one config value (TicksPerRealSecond = 1), so this is a simple
// fixed 1-second global loop rather than true per-restaurant variable-rate
// timing. Revisit if a second config with a different rate is ever built.
//
// Thin hosting wrapper only — actual per-tick logic lives in
// RestaurantTickProcessor (Application layer), which is directly unit-
// testable without BackgroundService/PeriodicTimer lifecycle concerns.
public sealed class SimulationTickService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SimulationTickService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<RestaurantTickProcessor>();
            await processor.AdvanceAllRestaurantsAsync(stoppingToken);
        }
    }
}
