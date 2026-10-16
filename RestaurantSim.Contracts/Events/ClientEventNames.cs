namespace RestaurantSim.Contracts.Events;

// Method names the server invokes on clients, one per pushed event. Shared by
// the Infrastructure handlers (send) and the client (subscribe) so the strings
// can't drift apart.
public static class ClientEventNames
{
    public const string GroupSeated = "GroupSeated";
}
