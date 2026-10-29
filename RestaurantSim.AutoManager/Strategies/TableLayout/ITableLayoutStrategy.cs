using RestaurantSim.Domain;

namespace RestaurantSim.AutoManager.Strategies.TableLayout;

public abstract record TableLayoutDecision;

public sealed record MergeClustersDecision(IReadOnlyList<int> ClusterIds) : TableLayoutDecision;

public sealed record SplitClusterDecision(int ClusterId) : TableLayoutDecision;

// Decides when tables are pushed together for a big party and when they go back to
// normal. Consulted before the seating strategy on every step of the seating pass.
public interface ITableLayoutStrategy
{
    TableLayoutDecision? DecideNextLayoutChange(Restaurant restaurant);
}
