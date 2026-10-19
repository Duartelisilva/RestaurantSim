using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Application.Commands.AdvanceMood;

public sealed class AdvanceMoodCommandHandler : IRequestHandler<AdvanceMoodCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;

    public AdvanceMoodCommandHandler(IRestaurantRepository repository, IPresetRepository<RestaurantConfig> presetRepository)
    {
        _repository = repository;
        _presetRepository = presetRepository;
    }

    public async Task<Result> Handle(AdvanceMoodCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        // Defaults to 1.0 (no change vs base curve) if unset — presets doc's
        // own stated convention.
        var patienceDecayBaseline = config.PatienceDecayBaseline ?? 1.0f;

        // Queue wait — every present member of every queued group (business
        // logic §1.2). Not seated yet, so Natural recovery never applies here.
        foreach (var group in restaurant.CustomerService.Queue)
        {
            foreach (var member in group.Members)
            {
                var decayRate = TickRates.PerTick(config.QueueWaitMoodDecayPerSecond, config.TicksPerRealSecond) * DecayModifier(member.PatienceLevel, patienceDecayBaseline);
                member.Mood = Math.Clamp(member.Mood + decayRate, 0, 100);
            }
        }

        // Seated groups — per member, per mechanics doc §2/business logic §1.6:
        // Course-wait decay if they have any of their own OrderItems not yet
        // Served, Natural recovery otherwise (including before ordering
        // anything, or once everything they ordered has arrived). Absent
        // members (bathroom, mechanics doc §12) are frozen — skipped entirely.
        foreach (var group in restaurant.CustomerService.ActiveGroups)
        {
            var order = group.CurrentOrderId is int orderId
                ? restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == orderId)
                : null;

            foreach (var member in group.Members)
            {
                if (!member.IsPresentAtTable) continue;

                var isWaitingOnCourse = order?.Items.Any(i => i.OwnerId == member.CustomerId && i.Status != Domain.KitchenOps.OrderItemStatus.Served) ?? false;

                if (isWaitingOnCourse)
                {
                    var decayRate = TickRates.PerTick(config.CourseWaitMoodDecayPerSecond, config.TicksPerRealSecond) * DecayModifier(member.PatienceLevel, patienceDecayBaseline);
                    member.Mood = Math.Clamp(member.Mood + decayRate, 0, 100);
                }
                else
                {
                    var recoveryRate = TickRates.PerTick(config.NaturalMoodRecoveryPerSecond, config.TicksPerRealSecond) * RecoveryModifier(member.PatienceLevel);
                    member.Mood = Math.Clamp(member.Mood + recoveryRate, 0, 100);
                }
            }
        }

        // No domain event fired per tick — architecture doc §3 explicitly warns
        // against broadcasting noisy, high-frequency state (every present
        // customer, every tick, would be extremely chatty for SignalR).
        // MoodChanged (named in CLAUDE.md's DomainEvents list) is reserved for
        // a future, more targeted use — e.g. firing only when a customer
        // crosses a Mood TIER boundary (mechanics doc §2), which is far rarer
        // and more meaningful for UI purposes than every continuous tick delta.
        await _repository.SaveAsync(restaurant, cancellationToken);

        return Result.Success();
    }

    // mechanics doc §2 — range [0.4, 1.6] at PatienceDecayBaseline=1.0.
    private static float DecayModifier(float patienceLevel, float patienceDecayBaseline) =>
        (1.6f - 1.2f * patienceLevel) * patienceDecayBaseline;

    // mechanics doc §2 — range [0.4, 1.6].
    private static float RecoveryModifier(float patienceLevel) =>
        0.4f + 1.2f * patienceLevel;
}
