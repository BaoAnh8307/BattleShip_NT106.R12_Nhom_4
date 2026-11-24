using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>Cách màn chiến đấu đóng lại.</summary>
public enum BattleExit
{
    /// <summary>Ván xong hoặc người chơi rời trận: quay về sảnh.</summary>
    BackToLobby,
    /// <summary>Mất kết nối tới server: nên quay về màn đăng nhập.</summary>
    ConnectionLost
}

/// <summary>
/// Những gì <see cref="BattleController"/> cần từ màn chiến đấu. Tách ra để kiểm thử controller
/// mà không cần mở form thật. <see cref="FormBattleView"/> là bản cài đặt cho FormBattle.
/// </summary>
public interface IBattleView
{
    bool IsGameOver { get; }

    /// <summary>Người chơi bấm một ô để bắn.</summary>
    event Action<int, int>? FireRequested;

    /// <summary>Người chơi đầu hàng (nút Đầu hàng hoặc thoát giữa ván).</summary>
    event Action? SurrenderRequested;

    /// <summary>Người chơi bấm Rời trận sau khi ván kết thúc.</summary>
    event Action? LeaveRequested;

    void SetTurn(bool isMyTurn);
    void ApplyMyShotResult(FireResultInfo result);
    void ApplyOpponentShot(FireResultInfo result);
    void EndGame(bool isWinner, string reason);
    void ShowError(string message);
    void Close(BattleExit exit);
}
