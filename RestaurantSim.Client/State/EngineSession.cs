using RestaurantSim.Client.Engine;
using RestaurantSim.Client.Time;
using RestaurantSim.Contracts.Events;

namespace RestaurantSim.Client.State;

// Orchestrates one play session: connect -> create -> join -> load the store,
// routes pushed events into the store, and recovers from connection drops by
// rejoining the restaurant group and refetching the snapshot (the group
// membership is lost on reconnect, and events pushed during the outage are
// gone — a fresh snapshot is the only correct recovery).
//
// Depends only on IEngineClient, so none of this assumes SignalR.
public sealed class EngineSession : IDisposable
{
    private const int MaxLogLines = 50;

    private readonly IEngineClient _engine;
    private readonly RestaurantStore _store;
    private readonly List<string> _log = new();

    private Guid? _restaurantId;
    private bool _starting;
    private bool _needsResync;

    public EngineSession(IEngineClient engine, RestaurantStore store)
    {
        _engine = engine;
        _store = store;

        _engine.ConnectionStateChanged += OnConnectionStateChanged;
        _engine.GroupArrived += OnGroupArrived;
        _engine.GroupAbandonedQueue += OnGroupAbandonedQueue;
        _engine.GroupSeated += OnGroupSeated;
        _engine.GroupLeft += OnGroupLeft;
        _engine.TableCleaned += OnTableCleaned;
        _store.Changed += RaiseChanged;
    }

    public string Status { get; private set; } = "Not connected";

    public ConnectionState Connection { get; private set; } = ConnectionState.Disconnected;

    public GameClock? Clock { get; private set; }

    public IReadOnlyList<string> Log => _log;

    public bool IsStarted => _starting || _restaurantId is not null;

    // True once the engine connection gave up (SignalR's automatic retries are
    // exhausted) while a restaurant was joined.
    public bool CanReconnect => _needsResync && Connection == ConnectionState.Disconnected;

    public event Action? Changed;

    public async Task StartAsync(string configId)
    {
        if (IsStarted)
            return;

        _starting = true;
        try
        {
            SetStatus("Connecting...");
            await _engine.ConnectAsync();

            SetStatus("Creating restaurant...");
            var created = await _engine.CreateRestaurantAsync(configId);
            if (!created.Succeeded)
            {
                SetStatus($"Create failed: {created.Error}");
                return;
            }

            await JoinAsync(created.Value);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            _starting = false;
            RaiseChanged();
        }
    }

    // Manual retry after the automatic reconnects gave up. Reconnecting the
    // transport raises Connected, which triggers the resync below.
    public async Task ReconnectAsync()
    {
        try
        {
            SetStatus("Reconnecting...");
            await _engine.ConnectAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Reconnect failed: {ex.Message}");
        }
    }

    private async Task<bool> JoinAsync(Guid restaurantId)
    {
        var joined = await _engine.JoinRestaurantAsync(restaurantId);
        if (joined is { Succeeded: true, Value: { } snapshot })
        {
            _restaurantId = restaurantId;
            Clock = new GameClock(
                snapshot.SimulationTick, snapshot.MinutesPerTick, snapshot.TicksPerRealSecond, DateTimeOffset.UtcNow);
            _store.Load(snapshot);
            SetStatus("Joined");
            return true;
        }

        SetStatus($"Join failed: {joined.Error}");
        return false;
    }

    private void OnConnectionStateChanged(ConnectionState state)
    {
        Connection = state;

        // Nothing to recover until a restaurant has actually been joined.
        if (_restaurantId is not { } restaurantId)
        {
            RaiseChanged();
            return;
        }

        switch (state)
        {
            case ConnectionState.Reconnecting:
                _needsResync = true;
                SetStatus("Connection lost — reconnecting...");
                break;

            case ConnectionState.Disconnected:
                _needsResync = true;
                SetStatus("Disconnected");
                break;

            case ConnectionState.Connected when _needsResync:
                _ = ResyncAsync(restaurantId);
                break;

            default:
                RaiseChanged();
                break;
        }
    }

    // Fire-and-forget from the connection callback; every failure ends up in
    // Status, never as an unobserved exception.
    private async Task ResyncAsync(Guid restaurantId)
    {
        try
        {
            SetStatus("Reconnected — resyncing...");
            if (await JoinAsync(restaurantId))
            {
                _needsResync = false;
                AddLog("resynced after reconnect");
            }
            // On failure JoinAsync already set the status; _needsResync stays true
            // so the next Connected transition tries again.
        }
        catch (Exception ex)
        {
            SetStatus($"Resync failed: {ex.Message}");
        }
    }

    private void OnGroupArrived(GroupArrivedDto dto)
    {
        if (_store.Apply(dto))
            AddLog($"[tick {dto.Tick}] group {dto.GroupId} arrived (party of {dto.PartySize})");
    }

    private void OnGroupAbandonedQueue(GroupAbandonedQueueDto dto)
    {
        if (_store.Apply(dto))
            AddLog($"[tick {dto.Tick}] group {dto.GroupId} left the queue");
    }

    private void OnGroupSeated(GroupSeatedDto dto)
    {
        if (_store.Apply(dto))
            AddLog($"[tick {dto.Tick}] group {dto.GroupId} seated at cluster {dto.ClusterId}");
    }

    private void OnGroupLeft(GroupLeftDto dto)
    {
        if (_store.Apply(dto))
            AddLog($"[tick {dto.Tick}] group {dto.GroupId} left cluster {dto.ClusterId}");
    }

    private void OnTableCleaned(TableCleanedDto dto)
    {
        if (_store.Apply(dto))
            AddLog($"[tick {dto.Tick}] table {dto.TableId} cleaned");
    }

    private void AddLog(string line)
    {
        _log.Add(line);
        if (_log.Count > MaxLogLines)
            _log.RemoveAt(0);

        RaiseChanged();
    }

    private void SetStatus(string status)
    {
        Status = status;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _engine.ConnectionStateChanged -= OnConnectionStateChanged;
        _engine.GroupArrived -= OnGroupArrived;
        _engine.GroupAbandonedQueue -= OnGroupAbandonedQueue;
        _engine.GroupSeated -= OnGroupSeated;
        _engine.GroupLeft -= OnGroupLeft;
        _engine.TableCleaned -= OnTableCleaned;
        _store.Changed -= RaiseChanged;
    }
}
