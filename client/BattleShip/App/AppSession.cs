using BattleShip.Network;

namespace BattleShip.App;

/// <summary>
/// Phiên làm việc dùng chung cho cả ứng dụng: MỘT <see cref="INetworkClient"/> và người dùng đang đăng nhập.
/// Các form lấy client từ đây, KHÔNG tự tạo client riêng (nếu không, sang form khác là mất kết nối và phiên đăng nhập).
/// </summary>
public static class AppSession
{
    private static INetworkClient? _client;

    public static AppConfig Config { get; private set; } = new();

    public static INetworkClient Client =>
        _client ?? throw new InvalidOperationException("AppSession.Start chưa được gọi.");

    /// <summary>Người chơi đang đăng nhập; null nếu chưa.</summary>
    public static LoginInfo? CurrentUser { get; set; }

    /// <summary>
    /// Gọi một lần khi khởi động, trên luồng giao diện (để event của client quay về đúng luồng).
    /// </summary>
    public static void Start(AppConfig? config = null, Func<AppConfig, INetworkClient>? factory = null)
    {
        Config = config ?? AppConfig.Load();
        _client = (factory ?? CreateClient)(Config);
        CurrentUser = null;
    }

    private static INetworkClient CreateClient(AppConfig config)
    {
        if (config.UseMock)
            return new MockNetworkClient(SynchronizationContext.Current);

        // TODO (nối client thật): thay dòng throw bên dưới bằng việc tạo NetworkClient của Nam, ví dụ:
        //     return new NetworkClient(SynchronizationContext.Current);
        // Khi đó đặt "UseMock": false trong appsettings.json.
        throw new NotSupportedException(
            "Chưa nối NetworkClient thật. Đặt \"UseMock\": true trong appsettings.json, hoặc sửa AppSession.CreateClient.");
    }

    /// <summary>Kết nối tới server (theo cấu hình) nếu chưa kết nối. Quá thời gian chờ thì ném lỗi.</summary>
    public static async Task EnsureConnectedAsync(TimeSpan? timeout = null)
    {
        if (Client.State != SessionState.Disconnected)
            return;

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        await Client.ConnectAsync(Config.ServerHost, Config.ServerPort, cts.Token);
    }

    /// <summary>Đăng xuất: ngắt kết nối và xóa người dùng hiện tại. Lần đăng nhập sau sẽ kết nối lại.</summary>
    public static async Task LogoutAsync()
    {
        CurrentUser = null;
        await Client.DisconnectAsync();
    }

    /// <summary>Dọn dẹp khi thoát ứng dụng.</summary>
    public static async Task ShutdownAsync()
    {
        CurrentUser = null;
        if (_client is null)
            return;

        await _client.DisposeAsync();
        _client = null;
    }
}
