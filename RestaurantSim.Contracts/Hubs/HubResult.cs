namespace RestaurantSim.Contracts.Hubs;

// Wire-level counterpart of Application's Result<T> (CLAUDE.md Error Handling):
// expected business failures travel as data, not as HubException.
public sealed record HubResult<T>(bool Succeeded, T? Value, string? Error)
{
    public static HubResult<T> Ok(T value) => new(true, value, null);

    public static HubResult<T> Fail(string error) => new(false, default, error);
}
