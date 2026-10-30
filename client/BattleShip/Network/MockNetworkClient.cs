using System.Text.RegularExpressions;

namespace BattleShip.Network;

/// <summary>
/// Client giả cho Bảo Anh code GUI mà không cần server. Giả lập đúng PROTOCOL.md v1:
/// máy trạng thái, mã lỗi, độ trễ, và một đối thủ máy (bot) bắn ngẫu nhiên.
///
/// Cách dùng trong Form (tạo trên luồng UI):
///     INetworkClient net = new MockNetworkClient(SynchronizationContext.Current);
///
/// Tài khoản có sẵn: demo / demo123 và test / test123.
///
/// Các hàm Simulate* và ForceNextError dùng để thử các ca khó của GUI (đối thủ thoát,
/// mất kết nối, lỗi từ server, màn thắng/thua) mà không phải chơi cả ván.
/// </summary>
public sealed class MockNetworkClient : INetworkClient
{
    private static readonly Regex UsernameRule = new("^[A-Za-z0-9_]{3,20}$", RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly UiDispatcher _ui;
    private readonly Random _rng;

    private readonly Dictionary<string, (int Id, string Password)> _users = new(StringComparer.Ordinal)
    {
        ["demo"] = (1, "demo123"),
        ["test"] = (2, "test123"),
    };
    private readonly List<HistoryEntry> _history = [];

    private SessionState _state = SessionState.Disconnected;
    private (string Code, string Message)? _forcedError;
    private int _nextUserId = 3;
    private string? _username;

    private MockBoard? _myBoard;   // bảng của mình (do PlaceShipsAsync dựng)
    private MockBoard? _botBoard;  // bảng của bot (ngẫu nhiên)
    private bool _youFirst;
    private Turn _turn = Turn.None;
    private DateTimeOffset _matchStart;

    /// <param name="uiContext">Truyền <c>SynchronizationContext.Current</c> trên luồng UI. Null: bắn event trực tiếp.</param>
    /// <param name="seed">Cố định để bot ngẫu nhiên lặp lại được (dùng khi test).</param>
    public MockNetworkClient(SynchronizationContext? uiContext = null, int? seed = null)
    {
        _ui = new UiDispatcher(uiContext);
        _rng = seed is null ? new Random() : new Random(seed.Value);

        var now = DateTimeOffset.Now;
        _history.Add(new HistoryEntry(1, "Bot_Duy", GameResult.Win, 41, now.AddDays(-2), now.AddDays(-2).AddMinutes(9)));
        _history.Add(new HistoryEntry(2, "Bot_Khoi", GameResult.Lose, 58, now.AddDays(-1), now.AddDays(-1).AddMinutes(12)));
    }

    // ----- Tùy chỉnh hành vi giả lập -----

    /// <summary>Độ trễ giả lập cho mỗi lệnh và mỗi nước đi của bot. Đặt Zero khi test.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>true/false: ép ai bắn trước. null: ngẫu nhiên như server thật.</summary>
    public bool? ForceYouFirst { get; set; }

    /// <summary>true: ConnectAsync sẽ thất bại, để thử màn báo lỗi kết nối.</summary>
    public bool SimulateConnectFailure { get; set; }

    public string OpponentName { get; set; } = "Bot_Nam";

    // ----- INetworkClient -----

    public SessionState State
    {
        get { lock (_gate) return _state; }
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
        await DelayAsync(cancellationToken);

        if (SimulateConnectFailure)
            throw new IOException($"Không kết nối được tới {host}:{port} (mô phỏng lỗi kết nối)");

        lock (_gate)
        {
            if (_state == SessionState.Disconnected)
                _state = SessionState.Connected;
        }
    }

    public Task DisconnectAsync()
    {
        lock (_gate)
        {
            ResetMatchLocked();
            _username = null;
            _state = SessionState.Disconnected;
        }
        return Task.CompletedTask;
    }

    public async Task RegisterAsync(string username, string password)
    {
        await BeginAsync();
        lock (_gate)
        {
            RequireStateLocked(SessionState.Connected);

            if (!UsernameRule.IsMatch(username))
                throw Err(ErrorCodes.InvalidUsername, "Tên đăng nhập gồm 3–20 ký tự: chữ không dấu, số hoặc dấu _");
            if (password.Length is < 6 or > 64)
                throw Err(ErrorCodes.WeakPassword, "Mật khẩu phải từ 6 đến 64 ký tự");
            if (_users.ContainsKey(username))
                throw Err(ErrorCodes.UsernameTaken, "Tên đăng nhập đã tồn tại");

            _users[username] = (_nextUserId++, password);
        }
    }

    public async Task<LoginInfo> LoginAsync(string username, string password)
    {
        await BeginAsync();
        lock (_gate)
        {
            RequireStateLocked(SessionState.Connected);

            if (!_users.TryGetValue(username, out var user) || user.Password != password)
                throw Err(ErrorCodes.AuthFailed, "Sai tên đăng nhập hoặc mật khẩu");

            _username = username;
            _state = SessionState.Authed;
            return new LoginInfo(user.Id, username);
        }
    }

    public async Task FindMatchAsync()
    {
        await BeginAsync();
        lock (_gate)
        {
            RequireStateLocked(SessionState.Authed);
            _state = SessionState.InQueue;
        }
        _ = Task.Run(MatchmakingAsync);
    }

    public async Task PlaceShipsAsync(IReadOnlyList<ShipPlacement> ships)
    {
        await BeginAsync();
        lock (_gate)
        {
            RequireStateLocked(SessionState.Placing);

            var problem = Fleet.Validate(ships);
            if (problem is not null)
                throw Err(ErrorCodes.InvalidShips, problem);

            _myBoard = new MockBoard(ships);
        }
    }

    public async Task ReadyAsync()
    {
        await BeginAsync();
        lock (_gate)
        {
            RequireStateLocked(SessionState.Placing);
            if (_myBoard is null)
                throw Err(ErrorCodes.NotPlaced, "Bạn chưa đặt tàu");
            _state = SessionState.Ready;
        }
        _ = Task.Run(StartGameAsync);
    }

    public async Task FireAsync(int x, int y)
    {
        await BeginAsync();

        FireResultInfo result;
        GameOverInfo? over = null;
        bool botNext;

        lock (_gate)
        {
            // Thứ tự kiểm tra: trạng thái, lượt, tọa độ, ô đã bắn. Lỗi nào cũng KHÔNG mất lượt.
            RequireStateLocked(SessionState.Playing);
            if (_turn != Turn.You)
                throw Err(ErrorCodes.NotYourTurn, "Chưa đến lượt của bạn");
            if (!Fleet.InBounds(x, y))
                throw Err(ErrorCodes.OutOfBounds, "Tọa độ nằm ngoài bảng");

            var board = _botBoard!;
            var cell = new Cell(x, y);
            if (board.AlreadyFired(cell))
                throw Err(ErrorCodes.AlreadyFired, "Ô này đã bắn rồi");

            var (shot, sunkShip, sunkCells) = board.Fire(cell);
            var gameOver = board.AllSunk;
            // HIT_KEEPS_TURN = true: trúng (HIT/SUNK) thì bắn tiếp, trượt thì đổi lượt.
            var next = gameOver ? Turn.None : shot == ShotResult.Miss ? Turn.Opponent : Turn.You;
            _turn = next;

            result = new FireResultInfo(x, y, shot, sunkShip, sunkCells, next, gameOver);
            if (gameOver)
                over = FinishLocked(GameResult.Win, GameOverReason.AllShipsSunk);
            botNext = next == Turn.Opponent;
        }

        Raise(() => OnFireResult?.Invoke(result));
        if (over is { } finished)
            Raise(() => OnGameOver?.Invoke(finished));
        else if (botNext)
            _ = Task.Run(RunBotTurnAsync);
    }

    public async Task SurrenderAsync()
    {
        await BeginAsync();

        GameOverInfo? over = null;
        lock (_gate)
        {
            RequireStateLocked(SessionState.Placing, SessionState.Ready, SessionState.Playing);

            if (_state == SessionState.Playing)
            {
                over = FinishLocked(GameResult.Lose, GameOverReason.Surrender);
            }
            else
            {
                // Chưa bắt đầu: coi như rời trận, không tính thắng thua.
                ResetMatchLocked();
                _state = SessionState.Authed;
            }
        }

        if (over is { } finished)
            Raise(() => OnGameOver?.Invoke(finished));
    }

    public async Task<StatsInfo> GetStatsAsync()
    {
        await BeginAsync();
        lock (_gate)
        {
            RequireStateLocked(SessionState.Authed);
            return new StatsInfo(
                _history.Count,
                _history.Count(h => h.Result == GameResult.Win),
                _history.Count(h => h.Result == GameResult.Lose));
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(int limit = 20)
    {
        await BeginAsync();
        lock (_gate)
        {
            RequireStateLocked(SessionState.Authed);
            return _history
                .OrderByDescending(h => h.MatchId)
                .Take(Math.Clamp(limit, 1, 50))
                .ToList();
        }
    }

    public async Task PingAsync() => await BeginAsync();

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    // ----- Hàm hỗ trợ thử GUI -----

    /// <summary>Lệnh kế tiếp sẽ thất bại với mã lỗi này (ví dụ ErrorCodes.AlreadyLoggedIn).</summary>
    public void ForceNextError(string code, string? message = null)
    {
        lock (_gate)
            _forcedError = (code, message ?? $"Lỗi mô phỏng: {code}");
    }

    /// <summary>Giả lập đối thủ thoát: đang bắn thì bạn thắng (DISCONNECT), chưa bắt đầu thì trận bị hủy.</summary>
    public void SimulateOpponentDisconnect()
    {
        GameOverInfo? over = null;
        var cancelled = false;

        lock (_gate)
        {
            if (_state == SessionState.Playing)
            {
                over = FinishLocked(GameResult.Win, GameOverReason.Disconnect);
            }
            else if (_state is SessionState.Placing or SessionState.Ready)
            {
                ResetMatchLocked();
                _state = SessionState.Authed;
                cancelled = true;
            }
        }

        if (over is { } finished)
            Raise(() => OnGameOver?.Invoke(finished));
        if (cancelled)
            Raise(() => OnMatchCancelled?.Invoke("OPPONENT_LEFT"));
    }

    /// <summary>Giả lập đứt kết nối tới server.</summary>
    public void SimulateConnectionLost()
    {
        lock (_gate)
        {
            ResetMatchLocked();
            _username = null;
            _state = SessionState.Disconnected;
        }
        Raise(() => OnDisconnected?.Invoke("Mất kết nối tới server (mô phỏng)"));
    }

    /// <summary>Kết thúc ván ngay để xem màn thắng/thua. Chỉ có tác dụng khi đang PLAYING.</summary>
    public void SimulateGameOver(GameResult result, GameOverReason reason = GameOverReason.AllShipsSunk)
    {
        GameOverInfo? over = null;
        lock (_gate)
        {
            if (_state == SessionState.Playing)
                over = FinishLocked(result, reason);
        }
        if (over is { } finished)
            Raise(() => OnGameOver?.Invoke(finished));
    }

    // ----- Giả lập server -----

    private async Task MatchmakingAsync()
    {
        await DelayAsync();
        await DelayAsync();

        MatchFoundInfo info;
        lock (_gate)
        {
            if (_state != SessionState.InQueue)
                return;

            _youFirst = ForceYouFirst ?? _rng.Next(2) == 0;
            _botBoard = MockBoard.CreateRandom(_rng);
            _myBoard = null;
            _turn = Turn.None;
            _matchStart = DateTimeOffset.Now;
            _state = SessionState.Placing;
            info = new MatchFoundInfo(OpponentName, _youFirst);
        }
        Raise(() => OnMatchFound?.Invoke(info));
    }

    private async Task StartGameAsync()
    {
        await DelayAsync();

        bool botFirst;
        lock (_gate)
        {
            if (_state != SessionState.Ready)
                return;

            _state = SessionState.Playing;
            _turn = _youFirst ? Turn.You : Turn.Opponent;
            botFirst = !_youFirst;
        }

        Raise(() => OnGameStart?.Invoke());
        if (botFirst)
            await RunBotTurnAsync();
    }

    /// <summary>Bot bắn liên tiếp cho đến khi trượt hoặc hết ván (HIT_KEEPS_TURN = true).</summary>
    private async Task RunBotTurnAsync()
    {
        while (true)
        {
            await DelayAsync();

            FireResultInfo shot;
            GameOverInfo? over = null;

            lock (_gate)
            {
                if (_state != SessionState.Playing || _turn != Turn.Opponent || _myBoard is null)
                    return;

                var cell = _myBoard.PickRandomUnfired(_rng);
                var (result, sunkShip, sunkCells) = _myBoard.Fire(cell);
                var gameOver = _myBoard.AllSunk;
                var next = gameOver ? Turn.None : result == ShotResult.Miss ? Turn.You : Turn.Opponent;
                _turn = next;

                shot = new FireResultInfo(cell.X, cell.Y, result, sunkShip, sunkCells, next, gameOver);
                if (gameOver)
                    over = FinishLocked(GameResult.Lose, GameOverReason.AllShipsSunk);
            }

            Raise(() => OnOpponentFired?.Invoke(shot));

            if (over is { } finished)
            {
                Raise(() => OnGameOver?.Invoke(finished));
                return;
            }
            if (shot.NextTurn != Turn.Opponent)
                return;
        }
    }

    // ----- Tiện ích nội bộ -----

    private Task DelayAsync(CancellationToken cancellationToken = default) =>
        Latency > TimeSpan.Zero ? Task.Delay(Latency, cancellationToken) : Task.CompletedTask;

    /// <summary>Phần chung của mọi lệnh: phải đã kết nối, chờ độ trễ, rồi áp lỗi ép sẵn nếu có.</summary>
    private async Task BeginAsync()
    {
        if (State == SessionState.Disconnected)
            throw new InvalidOperationException("Chưa kết nối tới server. Hãy gọi ConnectAsync trước.");

        await DelayAsync();

        (string Code, string Message)? forced;
        lock (_gate)
        {
            forced = _forcedError;
            _forcedError = null;
        }
        if (forced is { } error)
            throw Err(error.Code, error.Message);
    }

    private void RequireStateLocked(params SessionState[] allowed)
    {
        if (!allowed.Contains(_state))
            throw Err(ErrorCodes.BadState, $"Lệnh này không dùng được ở trạng thái {_state}");
    }

    private void ResetMatchLocked()
    {
        _myBoard = null;
        _botBoard = null;
        _turn = Turn.None;
    }

    /// <summary>Ghi kết quả vào lịch sử, dọn ván, về sảnh. Gọi khi đang giữ _gate.</summary>
    private GameOverInfo FinishLocked(GameResult result, GameOverReason reason)
    {
        var total = (_myBoard?.ShotsFired ?? 0) + (_botBoard?.ShotsFired ?? 0);
        _history.Add(new HistoryEntry(
            _history.Count + 1, OpponentName, result, total, _matchStart, DateTimeOffset.Now));

        ResetMatchLocked();
        _state = SessionState.Authed;
        return new GameOverInfo(result, reason, total);
    }

    private void Raise(Action action) => _ui.Raise(action);

    private static ServerErrorException Err(string code, string message) => new(code, message);
}
