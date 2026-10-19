using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Scheduling;

namespace RestaurantSim.Application.Commands.SpreadGroupMood;

public sealed class SpreadGroupMoodCommandHandler : IRequestHandler<SpreadGroupMoodCommand, Result>
{
    // mechanics doc §4 — locked constants, not tunable config fields. Expressed per
    // REAL SECOND (applied per tick as rate / TicksPerRealSecond), so the tick rate
    // never changes how fast Mood spreads in real time.
    private const float NormalSpreadRatePerSecond = 0.05f;
    private const float TippingPointSpreadRatePerSecond = 0.075f;
    private const float TippingPointMoodThreshold = 30f;
    private const float AdultInfluenceWeight = 1.5f;
    private const float ChildInfluenceWeight = 1.0f;

    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;

    public SpreadGroupMoodCommandHandler(IRestaurantRepository repository, IPresetRepository<RestaurantConfig> presetRepository)
    {
        _repository = repository;
        _presetRepository = presetRepository;
    }

    public async Task<Result> Handle(SpreadGroupMoodCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        // Applied to both queued and seated groups — the doc doesn't restrict
        // this to seated-only, and it's realistic that people waiting together
        // in line still affect each other's mood. IsPresentAtTable is always
        // true for queued members anyway (nothing toggles it pre-seating), so
        // the same presence filter works uniformly for both collections.
        foreach (var group in restaurant.CustomerService.Queue)
        {
            ApplySpread(group, config.TicksPerRealSecond);
        }

        foreach (var group in restaurant.CustomerService.ActiveGroups)
        {
            ApplySpread(group, config.TicksPerRealSecond);
        }

        await _repository.SaveAsync(restaurant, cancellationToken);

        return Result.Success();
    }

    private static void ApplySpread(CustomerGroup group, int ticksPerRealSecond)
    {
        var presentMembers = group.Members.Where(m => m.IsPresentAtTable).ToList();
        if (presentMembers.Count < 2) return; // no spread possible with 0 or 1 present member

        // Snapshot BEFORE computing any delta — every member's delta must be
        // computed off the SAME starting Mood values (this tick's already-
        // decayed Mood, per the locked execution order), not partially-spread
        // values from earlier members processed in the same pass.
        var moodSnapshot = presentMembers.ToDictionary(m => m.CustomerId, m => m.Mood);

        // "Full-group avg Mood" for the tipping-point check — interpreted as
        // present members only (absent members are frozen, not "in the room"
        // contributing to atmosphere). Not explicitly disambiguated in the doc.
        var fullGroupAvgMood = moodSnapshot.Values.Average();
        var spreadRate = TickRates.PerTick(
            fullGroupAvgMood < TippingPointMoodThreshold ? TippingPointSpreadRatePerSecond : NormalSpreadRatePerSecond,
            ticksPerRealSecond);

        var deltas = new Dictionary<int, float>();
        foreach (var member in presentMembers)
        {
            var others = presentMembers.Where(m => m.CustomerId != member.CustomerId).ToList();
            var totalWeight = others.Sum(o => o.IsAdult ? AdultInfluenceWeight : ChildInfluenceWeight);
            var weightedSum = others.Sum(o => moodSnapshot[o.CustomerId] * (o.IsAdult ? AdultInfluenceWeight : ChildInfluenceWeight));
            var groupWeightedAvg = weightedSum / totalWeight;

            deltas[member.CustomerId] = member.SusceptibilityToGroup * (groupWeightedAvg - moodSnapshot[member.CustomerId]) * spreadRate;
        }

        foreach (var member in presentMembers)
        {
            member.Mood = Math.Clamp(member.Mood + deltas[member.CustomerId], 0, 100);
        }
    }
}
