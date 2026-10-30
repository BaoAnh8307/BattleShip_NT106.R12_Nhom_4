namespace BattleShip.Network;

/// <summary>
/// Hợp đồng giữa lớp mạng (Nam) và giao diện (Bảo Anh).
/// Có hai bản cài đặt: <c>MockNetworkClient</c> (dữ liệu giả, không cần server)
/// và <c>NetworkClient</c> (nối server thật).
///
/// Quy ước:
/// - Các phương thức *Async tương ứng lệnh client gửi lên server. Server trả ERROR thì ném
///   <see cref="ServerErrorException"/>.
/// - Message server tự đẩy xuống (không do lệnh nào) và kết quả bắn được báo qua event.
/// - Event được bắn trên luồng giao diện (xem <see cref="UiDispatcher"/>).
/// </summary>
public interface INetworkClient : IAsyncDisposable
{
    SessionState State { get; }

    // ----- Event (server -> client) -----

    /// <summary>MATCH_FOUND: đã có đối thủ. Chuyển sang màn đặt tàu.</summary>
    event Action<MatchFoundInfo>? OnMatchFound;

    /// <summary>MATCH_CANCELLED: trận bị hủy trước khi bắt đầu (đối thủ rời). Tham số là lý do.</summary>
    event Action<string>? OnMatchCancelled;

    /// <summary>GAME_START: cả hai đã sẵn sàng. Ai bắn trước đã biết từ MatchFoundInfo.YouFirst.</summary>
    event Action? OnGameStart;

    /// <summary>FIRE_RESULT: kết quả phát bắn của MÌNH. Cập nhật bảng theo dõi.</summary>
    event Action<FireResultInfo>? OnFireResult;

    /// <summary>OPPONENT_FIRED: đối thủ vừa bắn vào bảng của MÌNH. Cập nhật bảng của mình.</summary>
    event Action<FireResultInfo>? OnOpponentFired;

    /// <summary>GAME_OVER: hết ván.</summary>
    event Action<GameOverInfo>? OnGameOver;

    /// <summary>Mất kết nối với server (socket đứt, PING timeout...). Tham số là lý do hiển thị.</summary>
    event Action<string>? OnDisconnected;

    // ----- Lệnh (client -> server) -----

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    /// <summary>REGISTER. Thành công thì vẫn ở CONNECTED, cần gọi LoginAsync riêng.</summary>
    Task RegisterAsync(string username, string password);

    /// <summary>LOGIN. Thành công thì sang AUTHED.</summary>
    Task<LoginInfo> LoginAsync(string username, string password);

    /// <summary>FIND_MATCH. Hoàn thành khi vào hàng đợi. MATCH_FOUND đến sau qua <see cref="OnMatchFound"/>.</summary>
    Task FindMatchAsync();

    /// <summary>PLACE_SHIPS. Được gọi lại nhiều lần khi còn ở PLACING; bản sau thay bản trước.</summary>
    Task PlaceShipsAsync(IReadOnlyList<ShipPlacement> ships);

    /// <summary>READY. GAME_START đến sau qua <see cref="OnGameStart"/> khi cả hai đã sẵn sàng.</summary>
    Task ReadyAsync();

    /// <summary>
    /// FIRE. Hoàn thành khi server nhận phát bắn. Kết quả đến qua <see cref="OnFireResult"/>.
    /// Sai lượt, ô đã bắn, ngoài bảng: ném <see cref="ServerErrorException"/> và KHÔNG mất lượt.
    /// </summary>
    Task FireAsync(int x, int y);

    /// <summary>
    /// SURRENDER. Đang PLAYING thì thua (nhận GAME_OVER). Đang PLACING/READY thì rời trận về sảnh.
    /// </summary>
    Task SurrenderAsync();

    Task<StatsInfo> GetStatsAsync();

    /// <summary>GET_HISTORY, mới nhất trước. limit mặc định 20, tối đa 50.</summary>
    Task<IReadOnlyList<HistoryEntry>> GetHistoryAsync(int limit = 20);

    Task PingAsync();
}
