using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleShip.Network;

/// <summary>TCP client tương thích giao thức server Python trong PROTOCOL.md v1.</summary>
public sealed class NetworkClient : INetworkClient
{
    private const int ProtocolVersion = 1;
    private const int MaxLineBytes = 64 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(10);
    private static readonly Regex UsernameRule = new("^[A-Za-z0-9_]{3,20}$", RegexOptions.Compiled);

    private readonly object _stateLock = new();
    private readonly UiDispatcher _ui;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private int _seq;
    private SessionState _state = SessionState.Disconnected;
    private DateTimeOffset _lastReceive = DateTimeOffset.UtcNow;
    private bool _disposed;

    public NetworkClient(SynchronizationContext? uiContext = null)
        => _ui = new UiDispatcher(uiContext);

    public SessionState State
    {
        get { lock (_stateLock) return _state; }
    }

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
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host không được để trống.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (State != SessionState.Disconnected)
            throw new InvalidOperationException("Client đã kết nối hoặc đang kết nối.");

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
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_tcp is null && State == SessionState.Disconnected)
            return;

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
        if (_heartbeatTask is not null)
        {
            try { await _heartbeatTask.ConfigureAwait(false); } catch { }
        }

        _lifetime?.Dispose();
        _lifetime = null;
        _receiveTask = null;
        _heartbeatTask = null;
    }

    public async Task RegisterAsync(string username, string password)
    {
        EnsureState(SessionState.Connected);
        ValidateCredentials(username, password);
        await SendRequestAsync(
            "REGISTER",
            new { username, password, version = ProtocolVersion },
            "ACK").ConfigureAwait(false);
        // Theo giao thức, đăng ký xong vẫn ở CONNECTED; phải đăng nhập riêng.
    }

    public async Task<LoginInfo> LoginAsync(string username, string password)
    {
        EnsureState(SessionState.Connected);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Tên đăng nhập và mật khẩu không được để trống.");

        JsonElement data = await SendRequestAsync(
            "LOGIN",
            new { username, password, version = ProtocolVersion },
            "LOGIN_OK").ConfigureAwait(false);

        var info = new LoginInfo(GetInt(data, "user_id"), GetString(data, "username"));
        SetState(SessionState.Authed);
        return info;
    }

    public async Task FindMatchAsync()
    {
        EnsureState(SessionState.Authed);
        // Đặt trạng thái trước khi gửi để không ghi đè PLACING nếu MATCH_FOUND đến rất nhanh.
        SetState(SessionState.InQueue);
        try
        {
            await SendRequestAsync("FIND_MATCH", new { }, "ACK").ConfigureAwait(false);
        }
        catch
        {
            if (State == SessionState.InQueue)
                SetState(SessionState.Authed);
            throw;
        }
    }

    public async Task PlaceShipsAsync(IReadOnlyList<ShipPlacement> ships)
    {
        EnsureState(SessionState.Placing);
        ArgumentNullException.ThrowIfNull(ships);

        var shipData = ships.Select(ship => new
        {
            name = ship.Name,
            x = ship.X,
            y = ship.Y,
            dir = ship.Dir == Direction.Horizontal ? "H" : "V"
        }).ToArray();

        await SendRequestAsync("PLACE_SHIPS", new { ships = shipData }, "ACK").ConfigureAwait(false);
    }

    public async Task ReadyAsync()
    {
        EnsureState(SessionState.Placing);
        // GAME_START có thể được server đẩy ngay sau ACK; tránh ghi đè PLAYING thành READY.
        SetState(SessionState.Ready);
        try
        {
            await SendRequestAsync("READY", new { }, "ACK").ConfigureAwait(false);
        }
        catch
        {
            if (State == SessionState.Ready)
                SetState(SessionState.Placing);
            throw;
        }
    }

    public async Task FireAsync(int x, int y)
    {
        EnsureState(SessionState.Playing);
        // FIRE_RESULT vừa là phản hồi có ref, vừa được phát thành event OnFireResult.
        await SendRequestAsync("FIRE", new { x, y }, "FIRE_RESULT").ConfigureAwait(false);
    }

    public async Task SurrenderAsync()
    {
        EnsureState(SessionState.Placing, SessionState.Ready, SessionState.Playing);
        SessionState current = State;

        if (current == SessionState.Playing)
        {
            // Khi đang chơi, server không trả ACK cho SURRENDER; server đẩy GAME_OVER.
            await SendWithoutResponseAsync("SURRENDER", new { }).ConfigureAwait(false);
        }
        else
        {
            await SendRequestAsync("SURRENDER", new { }, "ACK").ConfigureAwait(false);
            SetState(SessionState.Authed);
        }
    }

    public async Task<StatsInfo> GetStatsAsync()
    {
        EnsureState(SessionState.Authed);
        JsonElement data = await SendRequestAsync("GET_STATS", new { }, "STATS").ConfigureAwait(false);
        return new StatsInfo(
            GetInt(data, "played"),
            GetInt(data, "wins"),
            GetInt(data, "losses"));
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(int limit = 20)
    {
        EnsureState(SessionState.Authed);
        if (limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit phải từ 1 đến 50.");

        JsonElement data = await SendRequestAsync("GET_HISTORY", new { limit }, "HISTORY").ConfigureAwait(false);
        if (!data.TryGetProperty("matches", out JsonElement matches) || matches.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("HISTORY thiếu mảng matches.");

        return matches.EnumerateArray().Select(item => new HistoryEntry(
            GetInt(item, "match_id"),
            GetString(item, "opponent"),
            ParseGameResult(GetString(item, "result")),
            GetInt(item, "total_shots"),
            DateTimeOffset.Parse(GetString(item, "started_at")),
            DateTimeOffset.Parse(GetString(item, "ended_at")))).ToArray();
    }

    public async Task PingAsync()
    {
        EnsureConnected();
        await SendRequestAsync("PING", new { }, "PONG").ConfigureAwait(false);
    }

    private async Task<JsonElement> SendRequestAsync(string type, object data, string expectedType)
    {
        EnsureConnected();
        int seq = Interlocked.Increment(ref _seq);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(seq, completion))
            throw new InvalidOperationException("Trùng seq của request.");

        try
        {
            await WriteMessageAsync(type, data, seq).ConfigureAwait(false);
            JsonElement response = await completion.Task.WaitAsync(
                RequestTimeout,
                _lifetime?.Token ?? CancellationToken.None).ConfigureAwait(false);

            string actualType = GetString(response, "type");
            if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
                throw new InvalidDataException($"Server trả {actualType}, cần {expectedType}.");

            return response.GetProperty("data").Clone();
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Server không phản hồi lệnh {type} trong 30 giây.");
        }
        finally
        {
            _pending.TryRemove(seq, out _);
        }
    }

    private async Task SendWithoutResponseAsync(string type, object data)
    {
        EnsureConnected();
        int seq = Interlocked.Increment(ref _seq);
        await WriteMessageAsync(type, data, seq).ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(string type, object data, int seq)
    {
        EnsureConnected();
        NetworkStream stream = _stream!;
        string json = JsonSerializer.Serialize(new { type, seq, data });
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
        if (bytes.Length > MaxLineBytes)
            throw new InvalidOperationException("Message vượt giới hạn 64 KB.");

        CancellationToken token = _lifetime?.Token ?? CancellationToken.None;
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = await ReadLineAsync(_stream!, token).ConfigureAwait(false);
                if (line is null)
                    throw new IOException("Server đã đóng kết nối.");
                if (line.Length == 0)
                    continue;

                _lastReceive = DateTimeOffset.UtcNow;
                using JsonDocument document = JsonDocument.Parse(line);
                HandleMessage(document.RootElement);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Client đang chủ động đóng kết nối.
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                SetState(SessionState.Disconnected);
                FailPending(ex);
                _ui.Raise(() => OnDisconnected?.Invoke(ex.Message));
                try { _tcp?.Close(); } catch { }
                _stream = null;
                _tcp = null;
            }
        }
    }

    private void HandleMessage(JsonElement root)
    {
        string type = GetString(root, "type");
        int? reference = root.TryGetProperty("ref", out JsonElement refElement)
            && refElement.TryGetInt32(out int refValue)
                ? refValue
                : null;
        JsonElement data = root.TryGetProperty("data", out JsonElement dataElement)
            ? dataElement
            : default;

        if (type == "ERROR")
        {
            string code = GetString(data, "code");
            string message = GetString(data, "message");
            var exception = new ServerErrorException(code, message);
            if (reference.HasValue && _pending.TryGetValue(reference.Value, out var failed))
                failed.TrySetException(exception);
            return;
        }

        if (reference.HasValue && _pending.TryGetValue(reference.Value, out var pending))
        {
            if (type is "ACK" or "LOGIN_OK" or "STATS" or "HISTORY" or "PONG" or "FIRE_RESULT")
                pending.TrySetResult(root.Clone());
        }

        switch (type)
        {
            case "MATCH_FOUND":
                {
                    SetState(SessionState.Placing);
                    var match = new MatchFoundInfo(
                        GetString(data, "opponent"),
                        GetBool(data, "you_first"));
                    _ui.Raise(() => OnMatchFound?.Invoke(match));
                    break;
                }
            case "MATCH_CANCELLED":
                {
                    SetState(SessionState.Authed);
                    string reason = OptionalString(data, "reason") ?? "Đối thủ đã rời trận.";
                    _ui.Raise(() => OnMatchCancelled?.Invoke(reason));
                    break;
                }
            case "GAME_START":
                SetState(SessionState.Playing);
                _ui.Raise(() => OnGameStart?.Invoke());
                break;
            case "FIRE_RESULT":
                {
                    FireResultInfo result = ParseFireResult(data);
                    _ui.Raise(() => OnFireResult?.Invoke(result));
                    break;
                }
            case "OPPONENT_FIRED":
                {
                    FireResultInfo result = ParseFireResult(data);
                    _ui.Raise(() => OnOpponentFired?.Invoke(result));
                    break;
                }
            case "GAME_OVER":
                {
                    SetState(SessionState.Authed);
                    var result = new GameOverInfo(
                        ParseGameResult(GetString(data, "result")),
                        ParseGameOverReason(GetString(data, "reason")),
                        GetInt(data, "total_shots"));
                    _ui.Raise(() => OnGameOver?.Invoke(result));
                    break;
                }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(PingInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (DateTimeOffset.UtcNow - _lastReceive >= PingInterval)
                    await PingAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Client đang đóng kết nối.
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                SetState(SessionState.Disconnected);
                FailPending(ex);
                _ui.Raise(() => OnDisconnected?.Invoke(ex.Message));
                try { _tcp?.Close(); } catch { }
                _stream = null;
                _tcp = null;
            }
        }
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
            if (buffer.Length >= MaxLineBytes)
                throw new IOException("Message từ server vượt giới hạn 64 KB.");
            if (oneByte[0] != (byte)'\r')
                buffer.WriteByte(oneByte[0]);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static FireResultInfo ParseFireResult(JsonElement data)
    {
        var sunkCells = new List<Cell>();
        if (data.TryGetProperty("sunk_cells", out JsonElement cells) && cells.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement cell in cells.EnumerateArray())
                sunkCells.Add(new Cell(GetInt(cell, "x"), GetInt(cell, "y")));
        }

        string? nextTurn = OptionalString(data, "next_turn");
        Turn turn = nextTurn switch
        {
            "you" => Turn.You,
            "opponent" => Turn.Opponent,
            null => Turn.None,
            _ => throw new InvalidDataException($"next_turn không hợp lệ: {nextTurn}")
        };

        return new FireResultInfo(
            GetInt(data, "x"),
            GetInt(data, "y"),
            GetString(data, "result") switch
            {
                "MISS" => ShotResult.Miss,
                "HIT" => ShotResult.Hit,
                "SUNK" => ShotResult.Sunk,
                var value => throw new InvalidDataException($"result không hợp lệ: {value}")
            },
            OptionalString(data, "sunk_ship"),
            sunkCells,
            turn,
            GetBool(data, "game_over"));
    }

    private static void ValidateCredentials(string username, string password)
    {
        if (!UsernameRule.IsMatch(username))
            throw new ArgumentException("Tên đăng nhập phải gồm 3–20 ký tự A-Z, a-z, 0-9 hoặc _.");
        if (password is null || password.Length is < 6 or > 64)
            throw new ArgumentException("Mật khẩu phải từ 6 đến 64 ký tự.");
    }

    private void EnsureConnected()
    {
        if (_stream is null || _tcp is null || !_tcp.Connected)
            throw new IOException("Chưa kết nối server.");
    }

    private void EnsureState(params SessionState[] allowed)
    {
        if (!allowed.Contains(State))
            throw new InvalidOperationException($"Lệnh không hợp lệ ở trạng thái {State}.");
    }

    private void SetState(SessionState state)
    {
        lock (_stateLock) _state = state;
    }

    private void FailPending(Exception exception)
    {
        foreach (var item in _pending.Values)
            item.TrySetException(exception);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NetworkClient));
    }

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"Thiếu hoặc sai kiểu trường {name}.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.TryGetInt32(out int number)
            ? number
            : throw new InvalidDataException($"Thiếu hoặc sai kiểu trường {name}.");

    private static bool GetBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : throw new InvalidDataException($"Thiếu hoặc sai kiểu trường {name}.");

    private static GameResult ParseGameResult(string value) => value switch
    {
        "WIN" => GameResult.Win,
        "LOSE" => GameResult.Lose,
        _ => throw new InvalidDataException($"result không hợp lệ: {value}")
    };

    private static GameOverReason ParseGameOverReason(string value) => value switch
    {
        "ALL_SHIPS_SUNK" => GameOverReason.AllShipsSunk,
        "SURRENDER" => GameOverReason.Surrender,
        "DISCONNECT" => GameOverReason.Disconnect,
        _ => throw new InvalidDataException($"reason không hợp lệ: {value}")
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true;
        _sendLock.Dispose();
    }
}
