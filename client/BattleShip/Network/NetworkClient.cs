using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BattleShip.Network;

/// <summary>
/// TCP client tương thích server demo: type/request_id/payload.
/// Server demo hiện chỉ hỗ trợ PING, JOIN_QUEUE, LEAVE_QUEUE và GET_STATUS.
/// </summary>
public sealed class NetworkClient : INetworkClient
{
    private const int MaxLineBytes = 16 * 1024;
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(10);
    private readonly UiDispatcher _ui;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _stateLock = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private long _requestNumber;
    private SessionState _state = SessionState.Disconnected;
    private DateTimeOffset _lastReceive = DateTimeOffset.UtcNow;
    private bool _disposed;

    public NetworkClient(SynchronizationContext? uiContext = null) => _ui = new UiDispatcher(uiContext);
    public SessionState State { get { lock (_stateLock) return _state; } }

    public event Action<MatchFoundInfo>? OnMatchFound;
    public event Action<string>? OnMatchCancelled;
    public event Action? OnGameStart;
    public event Action<FireResultInfo>? OnFireResult;
    public event Action<FireResultInfo>? OnOpponentFired;
    public event Action<GameOverInfo>? OnGameOver;
    public event Action<string>? OnDisconnected;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host không được để trống.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (State != SessionState.Disconnected) throw new InvalidOperationException("Client đã kết nối.");

        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            _tcp = tcp;
            _stream = tcp.GetStream();
            _lifetime = new CancellationTokenSource();
            SetState(SessionState.Connected);
            _lastReceive = DateTimeOffset.UtcNow;
            _receiveTask = ReceiveLoopAsync(_lifetime.Token);
            _heartbeatTask = HeartbeatLoopAsync(_lifetime.Token);
        }
        catch { tcp.Dispose(); throw; }
    }

    public async Task DisconnectAsync()
    {
        if (_tcp is null && State == SessionState.Disconnected) return;
        _lifetime?.Cancel();
        try { _tcp?.Client.Shutdown(SocketShutdown.Both); } catch { }
        _tcp?.Close();
        _stream?.Dispose();
        _stream = null;
        _tcp = null;
        SetState(SessionState.Disconnected);
        FailPending(new IOException("Đã ngắt kết nối."));
        if (_receiveTask is not null)
        {
            try { await _receiveTask.ConfigureAwait(false); } catch { }
        }
        _lifetime?.Dispose();
        _lifetime = null;
        _receiveTask = null;
        _heartbeatTask = null;
    }

    // Server demo chưa triển khai đăng ký/đăng nhập.
    public Task RegisterAsync(string username, string password) => throw NotSupported("REGISTER");
    public Task<LoginInfo> LoginAsync(string username, string password) => throw NotSupported<LoginInfo>("LOGIN");

    public async Task FindMatchAsync()
    {
        EnsureState(SessionState.Connected, SessionState.Authed);
        string displayName = MakeDisplayName();
        await SendRequestAsync("JOIN_QUEUE", new { display_name = displayName }, "QUEUE_JOINED").ConfigureAwait(false);
        SetState(SessionState.InQueue);
    }

    public Task PlaceShipsAsync(IReadOnlyList<ShipPlacement> ships) => throw NotSupported("PLACE_SHIPS");
    public Task ReadyAsync() => throw NotSupported("READY");
    public Task FireAsync(int x, int y) => throw NotSupported("FIRE");

    public async Task SurrenderAsync()
    {
        EnsureState(SessionState.InQueue);
        await SendRequestAsync("LEAVE_QUEUE", new { }, "QUEUE_LEFT").ConfigureAwait(false);
        SetState(SessionState.Connected);
    }

    public Task<StatsInfo> GetStatsAsync() => throw NotSupported<StatsInfo>("GET_STATS");
    public Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(int limit = 20) =>
        throw NotSupported<IReadOnlyList<HistoryEntry>>("GET_HISTORY");

    public async Task PingAsync()
    {
        EnsureConnected();
        await SendRequestAsync("PING", new { }, "PONG").ConfigureAwait(false);
    }

    private async Task<JsonElement> SendRequestAsync(string type, object payload, string expectedType)
    {
        var stream = _stream ?? throw new IOException("Chưa kết nối server.");
        string requestId = Interlocked.Increment(ref _requestNumber).ToString();
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion)) throw new InvalidOperationException("Không tạo được request_id.");

        try
        {
            // Envelope của server Python: type, request_id, payload.
            string json = JsonSerializer.Serialize(new { type, request_id = requestId, payload });
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
            if (bytes.Length > MaxLineBytes) throw new InvalidOperationException("Message vượt giới hạn 16 KB.");

            CancellationToken token = _lifetime?.Token ?? CancellationToken.None;
            await _sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            finally { _sendLock.Release(); }

            JsonElement response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            string actualType = ReadString(response, "type");
            if (actualType != expectedType)
                throw new InvalidDataException($"Server trả {actualType}, cần {expectedType}.");
            return response.Clone();
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Server không phản hồi lệnh {type} trong 15 giây.");
        }
        finally { _pending.TryRemove(requestId, out _); }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = await ReadLineAsync(_stream!, token).ConfigureAwait(false);
                if (line is null) throw new IOException("Server đã đóng kết nối.");
                if (line.Length == 0) continue;
                _lastReceive = DateTimeOffset.UtcNow;
                using var document = JsonDocument.Parse(line);
                HandleMessage(document.RootElement);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                SetState(SessionState.Disconnected);
                FailPending(ex);
                _ui.Raise(() => OnDisconnected?.Invoke(ex.Message));
                _tcp?.Close();
                _stream = null;
                _tcp = null;
            }
        }
    }

    private void HandleMessage(JsonElement root)
    {
        string type = ReadString(root, "type");
        string? requestId = root.TryGetProperty("request_id", out var id) ? id.GetString() : null;
        JsonElement payload = root.TryGetProperty("payload", out var p) ? p : default;

        if (type == "ERROR")
        {
            string code = ReadString(payload, "code");
            string message = ReadString(payload, "message");
            var error = new ServerErrorException(code, message);
            if (requestId is not null && _pending.TryGetValue(requestId, out var failed))
                failed.TrySetException(error);
            return;
        }

        if (requestId is not null && _pending.TryGetValue(requestId, out var pending))
            pending.TrySetResult(root.Clone());

        switch (type)
        {
            case "WELCOME":
                // Server gửi lời chào ngay khi TCP vừa kết nối.
                break;
            case "MATCH_FOUND":
                SetState(SessionState.Placing);
                string opponent = OptionalString(payload, "opponent") ?? "Đối thủ";
                // Server demo có role P1/P2, chưa có thông tin lượt bắn.
                bool first = OptionalString(payload, "role") == "P1";
                var match = new MatchFoundInfo(opponent, first);
                _ui.Raise(() => OnMatchFound?.Invoke(match));
                break;
            case "MATCH_ENDED":
                SetState(SessionState.Connected);
                string reason = OptionalString(payload, "reason") ?? "Trận đã kết thúc.";
                _ui.Raise(() => OnMatchCancelled?.Invoke(reason));
                break;
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(PingInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                if (DateTimeOffset.UtcNow - _lastReceive >= PingInterval)
                    await PingAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { _ui.Raise(() => OnDisconnected?.Invoke(ex.Message)); }
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        byte[] oneByte = new byte[1];
        while (true)
        {
            int count = await stream.ReadAsync(oneByte.AsMemory(), token).ConfigureAwait(false);
            if (count == 0)
            {
                if (buffer.Length == 0) return null;
                throw new IOException("Server đóng kết nối giữa một message.");
            }
            if (oneByte[0] == (byte)'\n') break;
            if (buffer.Length >= MaxLineBytes) throw new IOException("Message từ server vượt giới hạn 16 KB.");
            if (oneByte[0] != (byte)'\r') buffer.WriteByte(oneByte[0]);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string MakeDisplayName()
    {
        string safe = new(Environment.UserName
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(24).ToArray());
        return safe.Length == 0 ? "Player" : safe;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"Thiếu hoặc sai trường {name}.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private void EnsureConnected()
    {
        if (_stream is null) throw new IOException("Chưa kết nối server.");
    }

    private void EnsureState(params SessionState[] allowed)
    {
        if (!allowed.Contains(State))
            throw new InvalidOperationException($"Lệnh không hợp lệ ở trạng thái {State}.");
    }

    private void SetState(SessionState state) { lock (_stateLock) _state = state; }
    private void FailPending(Exception exception)
    {
        foreach (var item in _pending.Values) item.TrySetException(exception);
    }

    private static NotSupportedException NotSupported(string command) =>
        new($"{command} chưa được hỗ trợ bởi server demo hiện tại.");
    private static NotSupportedException NotSupported<T>(string command) => NotSupported(command);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkClient));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true;
        _sendLock.Dispose();
    }
}
