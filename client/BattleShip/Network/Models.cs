namespace BattleShip.Network;

// Các kiểu dữ liệu dùng chung giữa GUI và lớp mạng (bản thật của Nam và bản Mock).
// Khớp với docs/PROTOCOL.md v1. GUI chỉ làm việc với các kiểu này, KHÔNG đụng JSON.

/// <summary>Trạng thái phiên phía client, tương ứng máy trạng thái ở mục 4 của PROTOCOL.md.</summary>
public enum SessionState
{
    Disconnected, // chưa có kết nối TCP
    Connected,    // CONNECTED: đã kết nối, chưa đăng nhập
    Authed,       // AUTHED: ở sảnh
    InQueue,      // IN_QUEUE: đang chờ ghép trận
    Placing,      // PLACING: đang đặt tàu
    Ready,        // READY: đã bấm Sẵn sàng, chờ đối thủ
    Playing       // PLAYING: đang bắn nhau
}

/// <summary>Hướng đặt tàu. Trên dây: "H" và "V".</summary>
public enum Direction { Horizontal, Vertical }

/// <summary>Kết quả một phát bắn. Trên dây: "MISS", "HIT", "SUNK".</summary>
public enum ShotResult { Miss, Hit, Sunk }

/// <summary>Lượt tiếp theo tính theo người nhận. None khi ván đã kết thúc (next_turn = null).</summary>
public enum Turn { You, Opponent, None }

public enum GameResult { Win, Lose }

/// <summary>Lý do kết thúc ván. Trên dây: ALL_SHIPS_SUNK, SURRENDER, DISCONNECT.</summary>
public enum GameOverReason { AllShipsSunk, Surrender, Disconnect }

/// <summary>Một ô trên bảng. X là cột, Y là hàng, gốc (0,0) ở góc trên trái.</summary>
public readonly record struct Cell(int X, int Y);

/// <summary>Vị trí một tàu: ô đầu (trái nhất nếu ngang, trên cùng nếu dọc) và hướng.</summary>
public sealed record ShipPlacement(string Name, int X, int Y, Direction Dir);

/// <summary>Thông tin cố định của một loại tàu.</summary>
public sealed record ShipSpec(string Name, int Length, string DisplayName);

public sealed record LoginInfo(int UserId, string Username);

public sealed record MatchFoundInfo(string Opponent, bool YouFirst);

/// <summary>
/// Dùng cho cả FIRE_RESULT (mình bắn) và OPPONENT_FIRED (đối thủ bắn vào mình).
/// NextTurn tính theo người nhận.
/// </summary>
public sealed record FireResultInfo(
    int X,
    int Y,
    ShotResult Result,
    string? SunkShip,
    IReadOnlyList<Cell> SunkCells,
    Turn NextTurn,
    bool GameOver);

public sealed record GameOverInfo(GameResult Result, GameOverReason Reason, int TotalShots);

public sealed record StatsInfo(int Played, int Wins, int Losses);

public sealed record HistoryEntry(
    int MatchId,
    string Opponent,
    GameResult Result,
    int TotalShots,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

/// <summary>Mã lỗi, khớp mục 6 của PROTOCOL.md.</summary>
public static class ErrorCodes
{
    public const string BadJson = "BAD_JSON";
    public const string UnknownType = "UNKNOWN_TYPE";
    public const string InvalidParams = "INVALID_PARAMS";
    public const string LineTooLong = "LINE_TOO_LONG";
    public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
    public const string BadState = "BAD_STATE";
    public const string AuthFailed = "AUTH_FAILED";
    public const string AlreadyLoggedIn = "ALREADY_LOGGED_IN";
    public const string UsernameTaken = "USERNAME_TAKEN";
    public const string InvalidUsername = "INVALID_USERNAME";
    public const string WeakPassword = "WEAK_PASSWORD";
    public const string InvalidShips = "INVALID_SHIPS";
    public const string NotPlaced = "NOT_PLACED";
    public const string NotYourTurn = "NOT_YOUR_TURN";
    public const string OutOfBounds = "OUT_OF_BOUNDS";
    public const string AlreadyFired = "ALREADY_FIRED";
    public const string ServerError = "SERVER_ERROR";
}

/// <summary>
/// Server trả message ERROR. GUI bắt lỗi này, so sánh <see cref="Code"/> (không so sánh Message)
/// và hiển thị Message cho người chơi.
/// </summary>
public sealed class ServerErrorException : Exception
{
    public string Code { get; }

    public ServerErrorException(string code, string message) : base(message)
    {
        Code = code;
    }
}
