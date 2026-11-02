using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Components;

// What the UI says (and which colour class it uses) for a group's dining stage. The stage
// itself is decided by the server (DiningStageCalculator); this only words it.
public static class DiningLabels
{
    public static string Describe(string stage, string nextVisit) => stage switch
    {
        DiningStageNames.Ordering => nextVisit switch
        {
            DiningVisitNames.Drinks => "choosing drinks",
            DiningVisitNames.Main => "choosing the main course",
            DiningVisitNames.Refill => "deciding on a refill",
            DiningVisitNames.Dessert => "choosing dessert",
            _ => "ordering"
        },
        DiningStageNames.WaitingForFood => "waiting for food",
        DiningStageNames.Eating => "eating",
        DiningStageNames.Paying => "paying",
        DiningStageNames.Leaving => "leaving",
        _ => stage
    };

    // "stage-waitingforfood" etc. — styled in app.css next to the table state classes.
    public static string CssClass(string stage) => "stage-" + stage.ToLowerInvariant();
}
