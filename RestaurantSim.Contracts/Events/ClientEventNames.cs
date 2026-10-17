namespace RestaurantSim.Contracts.Events;

// Method names the server invokes on clients, one per pushed event. Shared by
// the Infrastructure handlers (send) and the client (subscribe) so the strings
// can't drift apart.
public static class ClientEventNames
{
    public const string GroupArrived = "GroupArrived";
    public const string GroupTurnedAway = "GroupTurnedAway";
    public const string GroupAbandonedQueue = "GroupAbandonedQueue";
    public const string GroupSeated = "GroupSeated";
    public const string GroupLeft = "GroupLeft";
    public const string TableCleaned = "TableCleaned";
    public const string BillPaid = "BillPaid";
}
