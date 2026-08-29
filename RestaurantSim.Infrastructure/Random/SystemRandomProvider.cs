using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Infrastructure.Random;

public sealed class SystemRandomProvider : IRandomProvider
{
    private readonly System.Random _random = new();

    public double NextDouble() => _random.NextDouble();

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
