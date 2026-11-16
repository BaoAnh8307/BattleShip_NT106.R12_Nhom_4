using System.Text.Json;

namespace BattleShip.App;

/// <summary>
/// Cấu hình của client, đọc từ file <c>appsettings.json</c> nằm cạnh file chạy.
/// Địa chỉ server và cổng KHÔNG nhập trên giao diện mà lấy từ đây.
/// Thiếu file hoặc file hỏng thì dùng giá trị mặc định.
/// </summary>
public sealed class AppConfig
{
    public const string FileName = "appsettings.json";
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 5000;

    /// <summary>true: dùng MockNetworkClient (server giả); false: dùng NetworkClient thật.</summary>
    public bool UseMock { get; init; } = true;

    public string ServerHost { get; init; } = DefaultHost;

    public int ServerPort { get; init; } = DefaultPort;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Đọc cấu hình từ chuỗi JSON. Chuỗi rỗng hoặc sai định dạng thì trả về mặc định.</summary>
    public static AppConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AppConfig();

        try
        {
            var raw = JsonSerializer.Deserialize<AppConfig>(json, Options);
            if (raw is null)
                return new AppConfig();

            return new AppConfig
            {
                UseMock = raw.UseMock,
                ServerHost = string.IsNullOrWhiteSpace(raw.ServerHost) ? DefaultHost : raw.ServerHost.Trim(),
                ServerPort = raw.ServerPort is > 0 and <= 65535 ? raw.ServerPort : DefaultPort,
            };
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
    }

    /// <summary>Đọc <c>appsettings.json</c> trong thư mục chỉ định (mặc định: thư mục chứa file chạy).</summary>
    public static AppConfig Load(string? directory = null)
    {
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new AppConfig();
        }
        catch (IOException)
        {
            return new AppConfig();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }
}
