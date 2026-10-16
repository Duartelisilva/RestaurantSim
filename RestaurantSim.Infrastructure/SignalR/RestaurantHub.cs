using MediatR;
using Microsoft.AspNetCore.SignalR;
using RestaurantSim.Application.Commands.CreateRestaurant;
using RestaurantSim.Application.Queries.GetRestaurantSnapshot;
using RestaurantSim.Contracts.Hubs;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Infrastructure.SignalR;

// Client-invoked one-shot requests go through hub methods on the existing
// connection, not REST (architecture doc §3). Event pushes to the per-restaurant
// group (group name = RestaurantId.ToString()) are done by notification handlers.
public sealed class RestaurantHub : Hub
{
    private readonly ISender _sender;

    public RestaurantHub(ISender sender)
    {
        _sender = sender;
    }

    public async Task<HubResult<Guid>> CreateRestaurantAsync(string configId)
    {
        var result = await _sender.Send(new CreateRestaurantCommand(configId), Context.ConnectionAborted);

        return result.IsSuccess
            ? HubResult<Guid>.Ok(result.Value)
            : HubResult<Guid>.Fail(result.Error ?? "Could not create restaurant.");
    }

    // Joins the group BEFORE fetching the snapshot: any event raised in between
    // is delivered (and may already be reflected in the snapshot) rather than
    // lost. The client drops events with a tick strictly BELOW the snapshot's
    // tick and applies equal-tick events idempotently — several commands run
    // within one tick, so an equal tick may fall before or after the snapshot.
    public async Task<HubResult<RestaurantSnapshotDto>> JoinRestaurantAsync(Guid restaurantId)
    {
        var groupName = restaurantId.ToString();
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName, Context.ConnectionAborted);

        var result = await _sender.Send(new GetRestaurantSnapshotQuery(restaurantId), Context.ConnectionAborted);

        if (result is { IsSuccess: true, Value: { } snapshot })
            return HubResult<RestaurantSnapshotDto>.Ok(snapshot);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName, Context.ConnectionAborted);
        return HubResult<RestaurantSnapshotDto>.Fail(result.Error ?? "Could not load restaurant.");
    }

    public Task LeaveRestaurantAsync(Guid restaurantId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, restaurantId.ToString(), Context.ConnectionAborted);
    }
}
