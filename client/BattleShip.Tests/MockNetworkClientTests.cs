using System.Threading.Channels;
using BattleShip.Network;

namespace BattleShip.Tests;

/// <summary>
/// Test cho MockNetworkClient (không cần server, không cần giao diện).
/// Chạy: dotnet test client
/// Các test dùng Latency = 0 và seed cố định nên chạy nhanh và lặp lại được.
/// </summary>
public class MockNetworkClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MockNetworkClient NewClient(int seed = 1, bool? youFirst = true) =>
        new(null, seed) { Latency = TimeSpan.Zero, ForceYouFirst = youFirst };

    private static ShipPlacement[] ValidFleet() =>
    [
        new("CARRIER", 0, 0, Direction.Horizontal),
        new("BATTLESHIP", 0, 1, Direction.Horizontal),
        new("CRUISER", 0, 2, Direction.Horizontal),
        new("SUBMARINE", 0, 3, Direction.Horizontal),
        new("DESTROYER", 0, 4, Direction.Horizontal),
    ];

    private static async Task<string> ErrorCodeOf(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<ServerErrorException>(action);
        return ex.Code;
    }

    private static async Task LoginAsDemoAsync(MockNetworkClient client)
    {
        await client.ConnectAsync("localhost", 5000);
        await client.LoginAsync("demo", "demo123");
    }

    /// <summary>Đưa client tới trạng thái PLAYING (đã ghép cặp, đặt tàu, sẵn sàng, GAME_START).</summary>
    private static async Task StartMatchAsync(MockNetworkClient client)
    {
        var found = new TaskCompletionSource<MatchFoundInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMatchFound += info => found.TrySetResult(info);
        client.OnGameStart += () => started.TrySetResult();

        await LoginAsDemoAsync(client);
        await client.FindMatchAsync();
        await found.Task.WaitAsync(Timeout);
        await client.PlaceShipsAsync(ValidFleet());
        await client.ReadyAsync();
        await started.Task.WaitAsync(Timeout);
    }

    // ----- Đăng ký, đăng nhập -----

    [Fact]
    public async Task Register_InvalidUsername_ThrowsInvalidUsername()
    {
        var client = NewClient();
        await client.ConnectAsync("localhost", 5000);
        Assert.Equal(ErrorCodes.InvalidUsername, await ErrorCodeOf(() => client.RegisterAsync("a b", "123456")));
    }

    [Fact]
    public async Task Register_WeakPassword_ThrowsWeakPassword()
    {
        var client = NewClient();
        await client.ConnectAsync("localhost", 5000);
        Assert.Equal(ErrorCodes.WeakPassword, await ErrorCodeOf(() => client.RegisterAsync("khoi01", "123")));
    }

    [Fact]
    public async Task Register_TakenUsername_ThrowsUsernameTaken()
    {
        var client = NewClient();
        await client.ConnectAsync("localhost", 5000);
        Assert.Equal(ErrorCodes.UsernameTaken, await ErrorCodeOf(() => client.RegisterAsync("demo", "123456")));
    }

    [Fact]
    public async Task Register_ThenLogin_Works()
    {
        var client = NewClient();
        await client.ConnectAsync("localhost", 5000);
        await client.RegisterAsync("khoi01", "123456");
        Assert.Equal(SessionState.Connected, client.State);

        var info = await client.LoginAsync("khoi01", "123456");
        Assert.Equal("khoi01", info.Username);
        Assert.Equal(SessionState.Authed, client.State);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsAuthFailed()
    {
        var client = NewClient();
        await client.ConnectAsync("localhost", 5000);
        Assert.Equal(ErrorCodes.AuthFailed, await ErrorCodeOf(() => client.LoginAsync("demo", "sai")));
    }

    [Fact]
    public async Task FindMatch_BeforeLogin_ThrowsBadState()
    {
        var client = NewClient();
        await client.ConnectAsync("localhost", 5000);
        Assert.Equal(ErrorCodes.BadState, await ErrorCodeOf(() => client.FindMatchAsync()));
    }

    [Fact]
    public async Task Command_BeforeConnect_ThrowsInvalidOperation()
    {
        var client = NewClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LoginAsync("demo", "demo123"));
    }

    // ----- Đặt tàu -----

    [Fact]
    public async Task PlaceShips_Overlapping_ThrowsInvalidShips()
    {
        var client = NewClient();
        var found = new TaskCompletionSource<MatchFoundInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMatchFound += info => found.TrySetResult(info);
        await LoginAsDemoAsync(client);
        await client.FindMatchAsync();
        await found.Task.WaitAsync(Timeout);

        var fleet = ValidFleet();
        fleet[1] = new ShipPlacement("BATTLESHIP", 0, 0, Direction.Vertical); // chồng lên CARRIER
        Assert.Equal(ErrorCodes.InvalidShips, await ErrorCodeOf(() => client.PlaceShipsAsync(fleet)));
    }

    [Fact]
    public async Task PlaceShips_OutOfBounds_ThrowsInvalidShips()
    {
        var client = NewClient();
        var found = new TaskCompletionSource<MatchFoundInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMatchFound += info => found.TrySetResult(info);
        await LoginAsDemoAsync(client);
        await client.FindMatchAsync();
        await found.Task.WaitAsync(Timeout);

        var fleet = ValidFleet();
        fleet[0] = new ShipPlacement("CARRIER", 7, 0, Direction.Horizontal); // x = 7..11 vượt bảng
        Assert.Equal(ErrorCodes.InvalidShips, await ErrorCodeOf(() => client.PlaceShipsAsync(fleet)));
    }

    [Fact]
    public async Task Ready_WithoutPlacing_ThrowsNotPlaced()
    {
        var client = NewClient();
        var found = new TaskCompletionSource<MatchFoundInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMatchFound += info => found.TrySetResult(info);
        await LoginAsDemoAsync(client);
        await client.FindMatchAsync();
        await found.Task.WaitAsync(Timeout);

        Assert.Equal(ErrorCodes.NotPlaced, await ErrorCodeOf(() => client.ReadyAsync()));
    }

    [Fact]
    public void Fleet_Validate_AcceptsAdjacentShips()
    {
        // Luật: tàu được phép đặt sát nhau (ValidFleet đặt các tàu ở hàng 0..4 liền nhau).
        Assert.Null(Fleet.Validate(ValidFleet()));
    }

    // ----- Ván chơi -----

    [Fact]
    public async Task Fire_OutOfBounds_ThrowsOutOfBounds()
    {
        var client = NewClient(youFirst: true);
        await StartMatchAsync(client);

        Assert.Equal(ErrorCodes.OutOfBounds, await ErrorCodeOf(() => client.FireAsync(10, 0)));
        Assert.Equal(ErrorCodes.OutOfBounds, await ErrorCodeOf(() => client.FireAsync(-1, 3)));
    }

    [Fact]
    public async Task Fire_BeforeGameStarts_ThrowsBadState()
    {
        var client = NewClient();
        await LoginAsDemoAsync(client);
        Assert.Equal(ErrorCodes.BadState, await ErrorCodeOf(() => client.FireAsync(0, 0)));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task FullMatch_EndsWithGameOver_AndReturnsToLobby(int seed, bool youFirst)
    {
        var client = NewClient(seed, youFirst);
        var gameOver = new TaskCompletionSource<GameOverInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opponentShots = Channel.CreateUnbounded<FireResultInfo>();
        FireResultInfo? last = null;
        var myShots = 0;
        var botShots = 0;

        client.OnFireResult += info => { last = info; myShots++; };
        client.OnOpponentFired += info => { botShots++; opponentShots.Writer.TryWrite(info); };
        client.OnGameOver += info => gameOver.TrySetResult(info);

        await StartMatchAsync(client);

        // Chờ tới lượt mình (bot có thể đi trước, hoặc bắn liên tiếp khi trúng).
        async Task<bool> WaitForMyTurnAsync()
        {
            while (true)
            {
                var shot = await opponentShots.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
                if (shot.GameOver) return false;          // bot thắng
                if (shot.NextTurn == Turn.You) return true;
            }
        }

        var playing = youFirst || await WaitForMyTurnAsync();

        for (var y = 0; y < Fleet.BoardSize && playing; y++)
        {
            for (var x = 0; x < Fleet.BoardSize && playing; x++)
            {
                await client.FireAsync(x, y);
                if (last!.GameOver)
                    playing = false;                      // mình thắng
                else if (last.NextTurn == Turn.Opponent)
                    playing = await WaitForMyTurnAsync(); // false nếu bot thắng
            }
        }

        var over = await gameOver.Task.WaitAsync(Timeout);
        Assert.Equal(GameOverReason.AllShipsSunk, over.Reason);
        Assert.Equal(myShots + botShots, over.TotalShots);
        Assert.Equal(SessionState.Authed, client.State);
    }

    [Fact]
    public async Task Surrender_WhilePlaying_LosesAndRecordsHistory()
    {
        var client = NewClient(youFirst: true);
        var gameOver = new TaskCompletionSource<GameOverInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnGameOver += info => gameOver.TrySetResult(info);
        await StartMatchAsync(client);

        await client.SurrenderAsync();

        var over = await gameOver.Task.WaitAsync(Timeout);
        Assert.Equal(GameResult.Lose, over.Result);
        Assert.Equal(GameOverReason.Surrender, over.Reason);

        var stats = await client.GetStatsAsync();
        Assert.Equal(3, stats.Played);
        Assert.Equal(2, stats.Losses);
    }

    [Fact]
    public async Task Surrender_WhilePlacing_ReturnsToLobbyWithoutRecordingGame()
    {
        var client = NewClient();
        var found = new TaskCompletionSource<MatchFoundInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMatchFound += info => found.TrySetResult(info);
        await LoginAsDemoAsync(client);
        await client.FindMatchAsync();
        await found.Task.WaitAsync(Timeout);

        await client.SurrenderAsync();

        Assert.Equal(SessionState.Authed, client.State);
        Assert.Equal(2, (await client.GetStatsAsync()).Played);
    }

    // ----- Hàm giả lập cho GUI -----

    [Fact]
    public async Task ForceNextError_FailsOnlyTheNextCommand()
    {
        var client = NewClient();
        await LoginAsDemoAsync(client);

        client.ForceNextError(ErrorCodes.ServerError, "boom");
        Assert.Equal(ErrorCodes.ServerError, await ErrorCodeOf(() => client.GetStatsAsync()));

        var stats = await client.GetStatsAsync();   // lần sau bình thường
        Assert.Equal(2, stats.Played);
    }

    [Fact]
    public async Task SimulateOpponentDisconnect_WhilePlaying_WinsByDisconnect()
    {
        var client = NewClient(youFirst: true);
        var gameOver = new TaskCompletionSource<GameOverInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnGameOver += info => gameOver.TrySetResult(info);
        await StartMatchAsync(client);

        client.SimulateOpponentDisconnect();

        var over = await gameOver.Task.WaitAsync(Timeout);
        Assert.Equal(GameResult.Win, over.Result);
        Assert.Equal(GameOverReason.Disconnect, over.Reason);
    }

    [Fact]
    public async Task SimulateConnectionLost_RaisesOnDisconnected()
    {
        var client = NewClient();
        string? reason = null;
        client.OnDisconnected += r => reason = r;
        await LoginAsDemoAsync(client);

        client.SimulateConnectionLost();

        Assert.Equal(SessionState.Disconnected, client.State);
        Assert.NotNull(reason);
    }

    [Fact]
    public async Task ConnectAsync_WithSimulatedFailure_Throws()
    {
        var client = NewClient();
        client.SimulateConnectFailure = true;
        await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync("localhost", 5000));
    }
}
