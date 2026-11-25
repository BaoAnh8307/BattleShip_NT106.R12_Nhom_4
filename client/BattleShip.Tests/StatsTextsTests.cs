using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Tests;

/// <summary>Kiểm thử phần chữ hiển thị của FormStats (không cần giao diện).</summary>
public class StatsTextsTests
{
    [Fact]
    public void StatTexts_ShowCounts()
    {
        var stats = new StatsInfo(12, 7, 5);

        Assert.Equal("Đã chơi: 12", StatsTexts.PlayedText(stats));
        Assert.Equal("Thắng: 7", StatsTexts.WinsText(stats));
        Assert.Equal("Thua: 5", StatsTexts.LossesText(stats));
    }

    [Fact]
    public void ResultText_IsVietnamese()
    {
        Assert.Equal("Thắng", StatsTexts.ResultText(GameResult.Win));
        Assert.Equal("Thua", StatsTexts.ResultText(GameResult.Lose));
    }

    [Fact]
    public void TimeText_ConvertsFromUtcToGivenZone()
    {
        var utc = new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);
        var vietnam = TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");

        Assert.Equal("16/11/2026 09:30", StatsTexts.TimeText(utc, TimeZoneInfo.Utc));
        Assert.Equal("16/11/2026 16:30", StatsTexts.TimeText(utc, vietnam));
    }

    [Fact]
    public void TimeText_RollsOverToNextDay()
    {
        var utc = new DateTimeOffset(2026, 11, 16, 20, 5, 0, TimeSpan.Zero);
        var vietnam = TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");

        Assert.Equal("17/11/2026 03:05", StatsTexts.TimeText(utc, vietnam));
    }
}
