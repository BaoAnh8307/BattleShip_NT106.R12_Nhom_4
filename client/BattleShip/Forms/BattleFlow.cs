using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>
/// Mở màn chiến đấu đã nối mạng. Gọi sau khi FormPlaceShips đóng với <see cref="DialogResult.OK"/>:
/// <code>
/// var result = await BattleFlow.RunAsync(this, place.Match, place.PlacedShips);
/// // OK hoặc Cancel: về sảnh; Abort: mất kết nối, về màn đăng nhập.
/// </code>
/// Hãy gọi ngay sau khi form đặt tàu đóng (không await gì ở giữa) để không lỡ phát bắn đầu của đối thủ.
/// </summary>
public static class BattleFlow
{
    /// <summary>Như <see cref="RunAsync"/> nhưng không chờ lệnh đầu hàng (nếu có) gửi xong.</summary>
    public static DialogResult Run(IWin32Window? owner, MatchFoundInfo match, IReadOnlyList<ShipPlacement> ships) =>
        Show(owner, match, ships).Result;

    /// <summary>Mở màn chiến đấu; khi đóng, chờ các lệnh còn dang dở (ví dụ đầu hàng) gửi xong rồi mới trả về.</summary>
    public static async Task<DialogResult> RunAsync(IWin32Window? owner, MatchFoundInfo match, IReadOnlyList<ShipPlacement> ships)
    {
        var (result, pending) = Show(owner, match, ships);
        await pending;
        return result;
    }

    private static (DialogResult Result, Task Pending) Show(IWin32Window? owner, MatchFoundInfo match, IReadOnlyList<ShipPlacement> ships)
    {
        var playerName = AppSession.CurrentUser?.Username ?? "Bạn";
        using var form = new FormBattle(ships, playerName, match.Opponent);
        var view = new FormBattleView(form);
        using var controller = new BattleController(view, AppSession.Client, match);
        controller.Start();

        var result = owner is null ? form.ShowDialog() : form.ShowDialog(owner);
        return (result, controller.Completion);
    }
}
