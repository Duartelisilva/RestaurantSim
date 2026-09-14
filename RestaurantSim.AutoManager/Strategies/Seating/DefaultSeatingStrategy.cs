namespace RestaurantSim.AutoManager.Strategies.Seating;

using RestaurantSim.Domain;

// Greedy best-fit, not strict FIFO (business logic §1.2/§1.3): walks the
// queue in arrival order, and for the FIRST group that fits ANY currently
// free cluster, assigns the SMALLEST such cluster — preserving larger
// tables for larger groups. Matches the doc's own example: a 4-person
// family is skipped in favor of a 2-person couple further back in the
// queue if only a 2-top is currently free.
//
// Reservation-based priority (business logic §1.1: "Reserved parties
// skip/priority the queue") is NOT implemented — no command anywhere
// creates a Reservation or routes a reserved group into the queue yet, so
// there's nothing for this strategy to prioritize. Flagged for revisit
// once Reservations are built.
public sealed class DefaultSeatingStrategy : ISeatingStrategy
{
    public (int GroupId, int ClusterId)? DecideNextSeating(Restaurant restaurant)
    {
        foreach (var group in restaurant.CustomerService.Queue)
        {
            var fittingCluster = restaurant.Floor.Clusters
                .Where(c => c.CanSeat(group.PartySize, restaurant.Floor.Tables, restaurant.CustomerService.Reservations, restaurant.SimulationTick))
                .OrderBy(c => c.Capacity) // smallest fitting table first
                .FirstOrDefault();

            if (fittingCluster is not null)
                return (group.GroupId, fittingCluster.ClusterId);
        }

        return null;
    }
}
