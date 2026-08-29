using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Domain.Tests.CustomerService;

// Test double — replays a fixed sequence of NextDouble() results so
// ArrivalGroupGenerator's output is fully deterministic in tests. Loops the
// sequence if more calls are made than values provided.
public sealed class ScriptedRandomProvider : IRandomProvider
{
    private readonly double[] _sequence;
    private int _index;

    public ScriptedRandomProvider(params double[] sequence)
    {
        _sequence = sequence;
    }

    public double NextDouble()
    {
        var value = _sequence[_index % _sequence.Length];
        _index++;
        return value;
    }

    public int Next(int minInclusive, int maxExclusive) =>
        minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
}
