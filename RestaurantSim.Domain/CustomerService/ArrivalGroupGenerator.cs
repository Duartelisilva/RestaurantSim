using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Domain.CustomerService;

// First-pass distributions — business logic §1.1 describes group-size skew and
// mechanics doc §1 describes per-customer trait generation only qualitatively.
// Every weight/ratio below is a proposed starting default, same treatment as
// PatienceDecayBaseline — not locked, revisit during playtesting, formalize
// into the presets/mechanics docs if these numbers hold up.
public sealed class ArrivalGroupGenerator
{
    private readonly IRandomProvider _random;

    private static readonly (int Size, double Weight)[] PartySizeWeights =
    {
        (1, 0.15), (2, 0.35), (3, 0.20), (4, 0.20), (5, 0.06), (6, 0.04)
    };

    public ArrivalGroupGenerator(IRandomProvider random)
    {
        _random = random;
    }

    // nextCustomerId: caller-supplied monotonic counter (Restaurant.CustomerService.
    // NextCustomerId++) — IDs are never reused once a customer leaves, avoiding
    // stale-reference bugs from a reassigned ID.
    public CustomerGroup Generate(int groupId, long arrivalTick, Func<int> nextCustomerId)
    {
        var partySize = PickPartySize();
        var members = new List<Customer>(partySize);

        for (var i = 0; i < partySize; i++)
        {
            members.Add(GenerateCustomer(customerId: nextCustomerId(), isFirstMember: i == 0));
        }

        return new CustomerGroup
        {
            GroupId = groupId,
            PartySize = partySize,
            Members = members,
            ArrivalTick = arrivalTick,
            LifecycleState = GroupLifecycleState.Queued,
            PayerId = members[0].CustomerId
        };
    }

    private int PickPartySize()
    {
        var roll = _random.NextDouble();
        var cumulative = 0.0;
        foreach (var (size, weight) in PartySizeWeights)
        {
            cumulative += weight;
            if (roll <= cumulative) return size;
        }

        return PartySizeWeights[^1].Size;
    }

    private Customer GenerateCustomer(int customerId, bool isFirstMember)
    {
        var isAdult = isFirstMember || _random.NextDouble() < 0.8;
        var isRegular = isAdult && _random.NextDouble() < 0.15;
        var spendingTendency = isAdult ? PickSpendingTendency() : SpendingTendency.Normal;
        var gender = _random.NextDouble() < 0.5 ? Gender.Male : Gender.Female;

        var patienceMean = isRegular ? 0.6 : 0.5;
        var patience = ClampedNormal(patienceMean, 0.15);
        var instability = ClampedNormal(0.5, 0.15);
        var susceptibility = ClampedNormal(0.5, 0.15);

        var mood = 40 + _random.NextDouble() * 20;
        if (isRegular) mood += 10;
        if (spendingTendency == SpendingTendency.Generous) mood += 5;
        mood = Math.Clamp(mood, 0, 100);

        return new Customer
        {
            CustomerId = customerId,
            IsAdult = isAdult,
            Mood = (float)mood,
            PatienceLevel = (float)patience,
            Instability = (float)instability,
            SusceptibilityToGroup = (float)susceptibility,
            SpendingTendency = spendingTendency,
            IsRegular = isRegular,
            Gender = gender
        };
    }

    private SpendingTendency PickSpendingTendency()
    {
        var roll = _random.NextDouble();
        return roll switch
        {
            < 0.30 => SpendingTendency.Budget,
            < 0.80 => SpendingTendency.Normal,
            _ => SpendingTendency.Generous
        };
    }

    private double ClampedNormal(double mean, double stdDev)
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return Math.Clamp(mean + stdDev * randStdNormal, 0, 1);
    }
}
