using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>
/// Nối màn chiến đấu với client mạng:
///  - thao tác của người chơi (bắn, đầu hàng, rời trận) -> lệnh gửi lên server;
///  - event từ server (FIRE_RESULT, OPPONENT_FIRED, GAME_OVER, mất kết nối) -> cập nhật màn chiến đấu.
/// Phải được tạo trên luồng giao diện, ngay sau khi màn đặt tàu đóng (để không lỡ phát bắn đầu của đối thủ),
/// rồi gọi <see cref="Start"/>.
/// </summary>
public sealed class BattleController : IDisposable
{
    private readonly IBattleView _view;
    private readonly INetworkClient _client;
    private readonly MatchFoundInfo _match;
    private readonly List<Task> _pending = [];
    private bool _disposed;

    public BattleController(IBattleView view, INetworkClient client, MatchFoundInfo match)
    {
        _view = view;
        _client = client;
        _match = match;

        _view.FireRequested += OnFireRequested;
        _view.SurrenderRequested += OnSurrenderRequested;
        _view.LeaveRequested += OnLeaveRequested;

        _client.OnFireResult += OnFireResult;
        _client.OnOpponentFired += OnOpponentFired;
        _client.OnGameOver += OnGameOver;
        _client.OnDisconnected += OnDisconnected;
    }

    /// <summary>Đặt lượt ban đầu theo <see cref="MatchFoundInfo.YouFirst"/>.</summary>
    public void Start() => _view.SetTurn(_match.YouFirst);

    /// <summary>Hoàn thành khi mọi lệnh đã gửi (bắn, đầu hàng) xong. Dùng để chờ trước khi quay về sảnh.</summary>
    public Task Completion
    {
        get
        {
            lock (_pending)
                return Task.WhenAll(_pending.ToArray());
        }
    }

    // ----- Người chơi -> server -----

    private void OnFireRequested(int x, int y) => Track(FireAsync(x, y));

    private void OnSurrenderRequested() => Track(SurrenderAsync());

    private void OnLeaveRequested() => _view.Close(BattleExit.BackToLobby);

    private void Track(Task task)
    {
        lock (_pending)
            _pending.Add(task);
    }

    public async Task FireAsync(int x, int y)
    {
        try
        {
            await _client.FireAsync(x, y);
        }
        catch (ServerErrorException ex)
        {
            if (_disposed || _view.IsGameOver)
                return;

            _view.ShowError(ErrorTexts.FromCode(ex.Code, ex.Message));
            // Lỗi phát bắn không làm mất lượt: nếu vẫn là lượt của mình thì mở lại bảng bắn.
            var stillMyTurn = ex.Code != ErrorCodes.NotYourTurn && ex.Code != ErrorCodes.BadState;
            _view.SetTurn(stillMyTurn);
        }
        catch (Exception ex)
        {
            // Lỗi mạng: việc mất kết nối được báo riêng qua OnDisconnected.
            if (!_disposed && !_view.IsGameOver)
                _view.ShowError(ErrorTexts.FromException(ex));
        }
    }

    public async Task SurrenderAsync()
    {
        try
        {
            await _client.SurrenderAsync();
        }
        catch (Exception)
        {
            // Đang mất kết nối thì thôi: server tự xử thua khi phát hiện.
        }
    }

    // ----- Server -> màn chiến đấu -----

    private void OnFireResult(FireResultInfo result)
    {
        if (_disposed)
            return;
        _view.ApplyMyShotResult(result);
        AfterShot(result);
    }

    private void OnOpponentFired(FireResultInfo result)
    {
        if (_disposed)
            return;
        _view.ApplyOpponentShot(result);
        AfterShot(result);
    }

    private void AfterShot(FireResultInfo result)
    {
        if (result.GameOver || _view.IsGameOver)
            return;
        _view.SetTurn(result.NextTurn == Turn.You);
    }

    private void OnGameOver(GameOverInfo info)
    {
        if (_disposed)
            return;
        _view.EndGame(info.Result == GameResult.Win, ReasonText(info));
    }

    private void OnDisconnected(string reason)
    {
        if (_disposed)
            return;
        _view.ShowError(reason);
        _view.Close(BattleExit.ConnectionLost);
    }

    /// <summary>Câu giải thích lý do kết thúc ván, tính theo góc nhìn của người chơi.</summary>
    public static string ReasonText(GameOverInfo info)
    {
        var win = info.Result == GameResult.Win;
        var text = info.Reason switch
        {
            GameOverReason.AllShipsSunk => win
                ? "Toàn bộ hạm đội đối thủ đã bị đánh chìm!"
                : "Toàn bộ hạm đội của bạn đã bị đánh chìm!",
            GameOverReason.Surrender => win ? "Đối thủ đã đầu hàng." : "Bạn đã đầu hàng.",
            GameOverReason.Disconnect => win ? "Đối thủ đã thoát hoặc mất kết nối." : "Bạn đã mất kết nối.",
            _ => win ? "Bạn đã thắng." : "Bạn đã thua.",
        };
        return $"{text} (Tổng số phát bắn: {info.TotalShots})";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _view.FireRequested -= OnFireRequested;
        _view.SurrenderRequested -= OnSurrenderRequested;
        _view.LeaveRequested -= OnLeaveRequested;

        _client.OnFireResult -= OnFireResult;
        _client.OnOpponentFired -= OnOpponentFired;
        _client.OnGameOver -= OnGameOver;
        _client.OnDisconnected -= OnDisconnected;
    }
}
