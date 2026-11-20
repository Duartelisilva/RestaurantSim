using RestaurantSim.Contracts.Events;
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
    public static string TurnedAway(string reason) => reason switch
    {
        TurnedAwayReasonNames.QueueFull => "queue full",
        TurnedAwayReasonNames.PartyTooLarge => "party too large for any table",
        _ => reason
    };

    // "T3 + T4" — table ids in order.
    public static string TableList(IEnumerable<int> tableIds) =>
        string.Join(" + ", tableIds.Order().Select(id => $"T{id}"));

    // "stage-waitingforfood" etc.
    public static string CssClass(string stage) => "stage-" + stage.ToLowerInvariant();
}
