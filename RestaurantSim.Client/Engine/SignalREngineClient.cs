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

        // Handlers only raise our event and return; anything slow (rejoining,
        // refetching) is the consumer's job and must not block SignalR's reconnect path.
        _connection.Reconnecting += _ => Raise(ConnectionState.Reconnecting);
        _connection.Reconnected += _ => Raise(ConnectionState.Connected);
        _connection.Closed += _ => Raise(ConnectionState.Disconnected);
    }

    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public event Action<ConnectionState>? ConnectionStateChanged;
    public event Action<GroupArrivedDto>? GroupArrived;
    public event Action<GroupAbandonedQueueDto>? GroupAbandonedQueue;
    public event Action<GroupSeatedDto>? GroupSeated;
    public event Action<GroupLeftDto>? GroupLeft;
    public event Action<TableCleanedDto>? TableCleaned;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State != HubConnectionState.Disconnected)
            return;

        await _connection.StartAsync(cancellationToken);
        ConnectionStateChanged?.Invoke(ConnectionState.Connected);
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

    private Task Raise(ConnectionState state)
    {
        ConnectionStateChanged?.Invoke(state);
        return Task.CompletedTask;
    }
}
