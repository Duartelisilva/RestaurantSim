namespace RestaurantSim.Domain.Abstractions;

// Abstraction over randomness so Arrival's group/customer generation (and any
// future probability roll — complaint chance, food quality, etc.) can be tested
// deterministically rather than depending on real System.Random output.
public interface IRandomProvider
{
    // [0, 1]
    double NextDouble();

    int Next(int minInclusive, int maxExclusive);
}
