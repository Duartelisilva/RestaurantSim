using Microsoft.AspNetCore.SignalR.Client;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Hubs;
using RestaurantSim.Contracts.Snapshot;

namespace RestaurantSim.Client.Engine;

public sealed class SignalREngineClient : IEngineClient
{
    private readonly HubConnection _connection;

    public SignalREngineClient(string hubUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<GroupArrivedDto>(ClientEventNames.GroupArrived, dto => GroupArrived?.Invoke(dto));
        _connection.On<GroupAbandonedQueueDto>(ClientEventNames.GroupAbandonedQueue, dto => GroupAbandonedQueue?.Invoke(dto));
        _connection.On<GroupSeatedDto>(ClientEventNames.GroupSeated, dto => GroupSeated?.Invoke(dto));
        _connection.On<GroupLeftDto>(ClientEventNames.GroupLeft, dto => GroupLeft?.Invoke(dto));
        _connection.On<TableCleanedDto>(ClientEventNames.TableCleaned, dto => TableCleaned?.Invoke(dto));
    }

    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public event Action<GroupArrivedDto>? GroupArrived;
    public event Action<GroupAbandonedQueueDto>? GroupAbandonedQueue;
    public event Action<GroupSeatedDto>? GroupSeated;
    public event Action<GroupLeftDto>? GroupLeft;
    public event Action<TableCleanedDto>? TableCleaned;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return _connection.State == HubConnectionState.Disconnected
            ? _connection.StartAsync(cancellationToken)
            : Task.CompletedTask;
    }

    public Task<HubResult<Guid>> CreateRestaurantAsync(string configId, CancellationToken cancellationToken = default)
    {
        return _connection.InvokeAsync<HubResult<Guid>>(
            RestaurantHubMethods.CreateRestaurant, configId, cancellationToken);
    }

    public Task<HubResult<RestaurantSnapshotDto>> JoinRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        return _connection.InvokeAsync<HubResult<RestaurantSnapshotDto>>(
            RestaurantHubMethods.JoinRestaurant, restaurantId, cancellationToken);
    }

    public Task LeaveRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        return _connection.InvokeAsync(
            RestaurantHubMethods.LeaveRestaurant, restaurantId, cancellationToken);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
