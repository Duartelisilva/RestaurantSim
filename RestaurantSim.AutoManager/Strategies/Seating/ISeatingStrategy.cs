using RestaurantSim.Domain;

namespace RestaurantSim.AutoManager.Strategies.Seating;

// Lives here, not Domain/Abstractions — per CLAUDE.md's locked folder layout,
// AutoManager strategy interfaces live alongside their default implementations
// in RestaurantSim.AutoManager, not with the project's other abstractions
// (IRestaurantRepository, IMenuRepository, etc.).
//
// Pure decision logic only — does NOT dispatch SeatGroupCommand itself. The
// caller (AutoManagerProcessor) takes the decision and sends the command,
// keeping the strategy swappable/testable in isolation (architecture doc §5).
public interface ISeatingStrategy
{
    // Returns the next (GroupId, ClusterId) pairing to seat, or null if no
    // valid seating decision can currently be made (empty queue, or no
    // queued group fits any currently-available cluster).
    (int GroupId, int ClusterId)? DecideNextSeating(Restaurant restaurant);
}
