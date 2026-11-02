namespace RestaurantSim.Domain.CustomerService;

// What a seated group is visibly doing right now. Derived, never stored — see
// DiningStageCalculator. One value per group, computed on the server so clients
// never have to combine PhaseWaitState and item statuses themselves.
public enum DiningStage
{
    // Waiting for / talking to the waiter about the next order (drinks, main, refill, dessert).
    Ordering,

    // Order taken; at least one item has not been served yet.
    WaitingForFood,

    // Everything ordered so far is on the table; the group is eating and drinking.
    Eating,

    // The bill visit is due or under way.
    Paying,

    // Paid and standing up to leave.
    Leaving
}
