namespace RestaurantSim.Contracts.Snapshot;

// Wire names of the dining enums. Contracts has no reference to Domain, so these
// strings are the contract; a test in Application.Tests pins them to the Domain enums'
// member names so they cannot drift apart.
public static class DiningStageNames
{
    public const string Ordering = "Ordering";
    public const string WaitingForFood = "WaitingForFood";
    public const string Eating = "Eating";
    public const string Paying = "Paying";
    public const string Leaving = "Leaving";
}

public static class DiningVisitNames
{
    public const string Drinks = "Drinks";
    public const string Main = "Main";
    public const string Refill = "Refill";
    public const string Dessert = "Dessert";
    public const string Bill = "Bill";
}
