using System.Text.RegularExpressions;

namespace BattleShip.App;

/// <summary>
/// Quy tắc nhập liệu khi đăng ký (khớp mục 5.2 của PROTOCOL.md). Kiểm tra ngay ở client cho nhanh;
/// server vẫn kiểm tra lại. Mỗi hàm trả về câu báo lỗi, hoặc null nếu hợp lệ.
/// </summary>
public static class InputRules
{
    public const int UsernameMin = 3;
    public const int UsernameMax = 20;
    public const int PasswordMin = 6;
    public const int PasswordMax = 64;

    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public static string? ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return "Hãy nhập tên đăng nhập.";

        var name = username.Trim();
        if (name.Length < UsernameMin || name.Length > UsernameMax)
            return $"Tên đăng nhập phải từ {UsernameMin} đến {UsernameMax} ký tự.";
        if (!UsernamePattern.IsMatch(name))
            return "Tên đăng nhập chỉ gồm chữ không dấu, số và dấu gạch dưới (_).";

        return null;
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Hãy nhập mật khẩu.";
        if (password.Length < PasswordMin || password.Length > PasswordMax)
            return $"Mật khẩu phải từ {PasswordMin} đến {PasswordMax} ký tự.";

        return null;
    }

    public static string? ValidateConfirm(string? password, string? confirm) =>
        password == confirm ? null : "Mật khẩu nhập lại không khớp.";
}
