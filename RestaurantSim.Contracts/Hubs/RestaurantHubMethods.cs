namespace RestaurantSim.Contracts.Hubs;

// Method names the client invokes on RestaurantHub. SignalR does not strip the
// "Async" suffix, so these are the literal C# method names. A test in
// Infrastructure.Tests asserts each one exists on the hub.
public static class RestaurantHubMethods
{
    public const string CreateRestaurant = "CreateRestaurantAsync";
    public const string JoinRestaurant = "JoinRestaurantAsync";
    public const string LeaveRestaurant = "LeaveRestaurantAsync";
}
