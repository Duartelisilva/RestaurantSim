using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Application.Commands.ProcessQueueAbandonment;

public sealed class ProcessQueueAbandonmentCommandHandler : IRequestHandler<ProcessQueueAbandonmentCommand, Result>
{
    // First-pass default if RestaurantConfig.QueueAbandonmentMoodThreshold is unset —
    // tied to the already-locked Mood tier table (mechanics doc §2: Very Low = 0-14),
    // not an arbitrary number. Same "defaults if unset" treatment as PatienceDecayBaseline.
    private const float DefaultAbandonmentMoodThreshold = 15f;

    // Flat penalty per abandonment — confirmed design: no "witnessed" scaling
    // (cut deliberately), no per-group-size scaling (not asked for). Not locked
    // in any doc yet — pure code constant, revisit if it needs to become a
    // RestaurantConfig field for per-restaurant tuning later.
    private const float ReputationPenaltyPerAbandonment = 0.5f;

    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IPublisher _publisher;

    public ProcessQueueAbandonmentCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(ProcessQueueAbandonmentCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var threshold = config.QueueAbandonmentMoodThreshold ?? DefaultAbandonmentMoodThreshold;

        var queueSnapshot = restaurant.CustomerService.Queue.ToList();
        var abandonedEvents = new List<GroupAbandonedQueue>();

        foreach (var group in queueSnapshot)
        {
            if (group.Members.Count == 0) continue; // guard against a malformed empty group

            var averageMood = group.Members.Average(m => m.Mood);
            if (averageMood >= threshold) continue;

            // Reputation takes a small, flat hit — no effect on other customers,
            // no complaints-book interaction (confirmed design). The penalty
            // reflects the restaurant's own service capacity being outpaced by
            // demand, not a witnessed social event.
            restaurant.CustomerService.Queue.Remove(group);
            restaurant.Reputation = MathF.Max(0, restaurant.Reputation - ReputationPenaltyPerAbandonment);

            abandonedEvents.Add(new GroupAbandonedQueue(restaurant.RestaurantId, group.GroupId, restaurant.SimulationTick));
        }

        if (abandonedEvents.Count == 0)
            return Result.Success();

        await _repository.SaveAsync(restaurant, cancellationToken);

        foreach (var domainEvent in abandonedEvents)
        {
            await _publisher.Publish(domainEvent, cancellationToken);
        }

        return Result.Success();
    }
}
