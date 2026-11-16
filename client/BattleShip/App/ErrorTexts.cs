using System.Net.Sockets;
using BattleShip.Network;

namespace BattleShip.App;

/// <summary>Đổi lỗi (từ server hoặc từ mạng) thành câu tiếng Việt hiển thị cho người chơi.</summary>
public static class ErrorTexts
{
    private static readonly Dictionary<string, string> ByCode = new()
    {
        [ErrorCodes.AuthFailed] = "Sai tên đăng nhập hoặc mật khẩu.",
        [ErrorCodes.AlreadyLoggedIn] = "Tài khoản này đang đăng nhập ở nơi khác.",
        [ErrorCodes.UsernameTaken] = "Tên đăng nhập đã tồn tại, hãy chọn tên khác.",
        [ErrorCodes.InvalidUsername] = "Tên đăng nhập phải từ 3 đến 20 ký tự, gồm chữ không dấu, số hoặc dấu _.",
        [ErrorCodes.WeakPassword] = "Mật khẩu phải từ 6 đến 64 ký tự.",
        [ErrorCodes.UnsupportedVersion] = "Phiên bản ứng dụng không khớp với server.",
        [ErrorCodes.BadState] = "Thao tác này không dùng được lúc này.",
        [ErrorCodes.ServerError] = "Server gặp lỗi, hãy thử lại sau.",
    };

    /// <summary>Câu báo lỗi cho một mã lỗi của giao thức; mã lạ thì dùng thông điệp server gửi kèm.</summary>
    public static string FromCode(string code, string? serverMessage = null)
    {
        if (ByCode.TryGetValue(code, out var text))
            return text;
        return string.IsNullOrWhiteSpace(serverMessage) ? $"Lỗi từ server ({code})." : serverMessage;
    }

    public static string FromException(Exception ex, AppConfig? config = null)
    {
        switch (ex)
        {
            case ServerErrorException serverError:
                return FromCode(serverError.Code, serverError.Message);
            case OperationCanceledException or TimeoutException:
                return "Hết thời gian chờ phản hồi từ server.";
            case IOException or SocketException:
                return config is null
                    ? "Không kết nối được tới server."
                    : $"Không kết nối được tới server ({config.ServerHost}:{config.ServerPort}). Hãy kiểm tra server đã chạy chưa.";
            case InvalidOperationException:
                return "Chưa kết nối tới server. Hãy thử lại.";
            default:
                return $"Đã xảy ra lỗi: {ex.Message}";
        }
    }
}
