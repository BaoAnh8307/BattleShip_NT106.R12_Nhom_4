using System.Globalization;
using BattleShip.Network;

namespace BattleShip.App;

/// <summary>
/// Đổi dữ liệu thống kê và lịch sử (StatsInfo, HistoryEntry) thành chữ hiển thị trên FormStats.
/// Tách riêng khỏi form để kiểm thử được mà không cần giao diện.
/// </summary>
public static class StatsTexts
{
    public static string PlayedText(StatsInfo stats) => $"Đã chơi: {stats.Played}";

    public static string WinsText(StatsInfo stats) => $"Thắng: {stats.Wins}";

    public static string LossesText(StatsInfo stats) => $"Thua: {stats.Losses}";

    public static string ResultText(GameResult result) => result switch
    {
        GameResult.Win => "Thắng",
        GameResult.Lose => "Thua",
        _ => result.ToString(),
    };

    /// <summary>
    /// Giờ bắt đầu ván theo múi giờ của máy, dạng 16/11/2026 09:30.
    /// Server gửi giờ UTC (PROTOCOL.md mục 3.5) nên phải đổi sang giờ địa phương.
    /// Tham số <paramref name="zone"/> chỉ để kiểm thử, bình thường để null.
    /// </summary>
    public static string TimeText(DateTimeOffset time, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTime(time, zone ?? TimeZoneInfo.Local)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
