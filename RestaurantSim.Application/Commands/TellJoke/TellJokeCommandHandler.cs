using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.TellJoke;

// Confirmed in chat: Mood does NOT gate success chance at all — success
// depends purely on charisma (already fatigue-adjusted). Repeated jokes
// getting worse falls entirely out of JokeFatigueMultiplier shrinking
// EffectiveCharisma, not a separate rule. RollTargetMood (named in mechanics
// doc §11) is consequently unused in this resolved model — Magnitude already
// scales by each present member's OWN tier independently, never the
// roll-target's mood either.
public sealed class TellJokeCommandHandler : IRequestHandler<TellJokeCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IRandomProvider _random;
    private readonly IPublisher _publisher;

    public TellJokeCommandHandler(IRestaurantRepository repository, IRandomProvider random, IPublisher publisher)
    {
        _repository = repository;
        _random = random;
        _publisher = publisher;
    }

    public async Task<Result> Handle(TellJokeCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var cluster = restaurant.Floor.Clusters.FirstOrDefault(c => c.ClusterId == request.ClusterId);
        if (cluster is null)
            return Result.Failure($"Cluster {request.ClusterId} does not exist.");

        if (cluster.OccupiedBy is not int groupId)
            return Result.Failure($"Cluster {request.ClusterId} is not currently occupied.");

        var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == groupId);
        if (group is null)
            return Result.Failure($"Group {groupId} is not currently active.");

        var presentMembers = group.Members.Where(m => m.IsPresentAtTable).ToList();
        if (presentMembers.Count == 0)
            return Result.Failure("No present members to joke with.");

        if (request.TargetCustomerId is int targetId && presentMembers.All(m => m.CustomerId != targetId))
            return Result.Failure($"Target customer {targetId} is not present at this table.");

        var waiter = cluster.AssignedWaiterId is int waiterId
            ? restaurant.StaffRoster.Staff.FirstOrDefault(s => s.StaffId == waiterId)
            : restaurant.StaffRoster.Staff.FirstOrDefault(s => s.Role == StaffRole.Waiter);
        if (waiter is null)
            return Result.Failure("No waiter is available to tell a joke.");

        var jokeFatigueMultiplier = Math.Max(0.2f, 1f - 0.25f * cluster.RecentJokeCount);
        var effectiveCharisma = waiter.Stats.Get(StatType.Charisma) * jokeFatigueMultiplier;

        var wasSuccessful = _random.NextDouble() < effectiveCharisma;

        foreach (var member in presentMembers)
        {
            var tier = MoodTierCalculator.GetTier(member.Mood);
            var (min, max) = GetDeltaRange(tier, wasSuccessful);
            var delta = min + (float)_random.NextDouble() * (max - min);

            if (request.TargetCustomerId == member.CustomerId)
                delta *= 1.5f;

            member.Mood = Math.Clamp(member.Mood + delta, 0, 100);
        }

        cluster.RecentJokeCount++;
        cluster.LastJokeTick = restaurant.SimulationTick;

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(
            new JokeTold(restaurant.RestaurantId, cluster.ClusterId, waiter.StaffId, request.TargetCustomerId, wasSuccessful, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }

    private static (float Min, float Max) GetDeltaRange(MoodTier tier, bool success) => (tier, success) switch
    {
        (MoodTier.VeryLow, true) => (10f, 20f),
        (MoodTier.Low, true) => (7f, 15f),
        (MoodTier.Mid, true) => (5f, 12f),
        (MoodTier.High, true) => (3f, 8f),
        (MoodTier.VeryLow, false) => (-14f, -5f),
        (MoodTier.Low, false) => (-11f, -4f),
        (MoodTier.Mid, false) => (-9f, -3f),
        (MoodTier.High, false) => (-6f, -2f),
        _ => (0f, 0f)
    };
}
