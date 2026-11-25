using System.Runtime.InteropServices;
using BattleShip.Network;

namespace BattleShip.Controls;

/// <summary>Quy ước đặt tên file ảnh tàu. Thuần chuỗi, không dùng giao diện nên kiểm thử được.</summary>
public static class ShipSpriteNaming
{
    public static string DirectionSuffix(Direction dir) => dir == Direction.Horizontal ? "H" : "V";

    /// <summary>Ảnh riêng theo tên tàu, ví dụ carrier_H.png.</summary>
    public static string NamedFile(string shipName, Direction dir) =>
        $"{shipName.ToLowerInvariant()}_{DirectionSuffix(dir)}.png";

    /// <summary>Ảnh chung theo độ dài, ví dụ len3_V.png.</summary>
    public static string LengthFile(int length, Direction dir) =>
        $"len{length}_{DirectionSuffix(dir)}.png";

    /// <summary>Thứ tự thử: ảnh theo tên trước, rồi ảnh theo độ dài.</summary>
    public static IReadOnlyList<string> Candidates(string shipName, int length, Direction dir) =>
        [NamedFile(shipName, dir), LengthFile(length, dir)];

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Ships");
}

/// <summary>
/// Nạp và lưu đệm ảnh tàu. Thiếu file hoặc file hỏng thì trả về null (người gọi tự vẽ khối màu thay thế).
/// Mỗi ảnh được sao chép vào Bitmap riêng, nên không khóa file trên đĩa.
/// Cần Dispose khi không dùng nữa để trả GDI+ handle; bản <see cref="Shared"/> thì Dispose khi thoát ứng dụng.
/// </summary>
public sealed class ShipSpriteCache : IDisposable
{
    private readonly Dictionary<string, Bitmap?> _images = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public ShipSpriteCache(string? folderPath = null)
    {
        FolderPath = folderPath ?? ShipSpriteNaming.DefaultDirectory;
    }

    /// <summary>Bản dùng chung cho cả ứng dụng (đọc từ Assets\Ships cạnh file chạy).</summary>
    public static ShipSpriteCache Shared { get; } = new();

    public string FolderPath { get; }

    /// <summary>Số ảnh đã nạp thành công vào bộ nhớ.</summary>
    public int LoadedCount => _images.Values.Count(image => image is not null);

    /// <summary>
    /// Tìm ảnh theo thứ tự: tên tàu rồi độ dài. Không có thì trả null. Ảnh trả về thuộc về cache,
    /// KHÔNG được Dispose ở nơi gọi.
    /// </summary>
    public Image? Get(string shipName, int length, Direction dir)
    {
        if (_disposed)
            return null;

        foreach (var file in ShipSpriteNaming.Candidates(shipName, length, dir))
        {
            var image = Load(file);
            if (image is not null)
                return image;
        }
        return null;
    }

    private Bitmap? Load(string file)
    {
        if (_images.TryGetValue(file, out var cached))
            return cached; // cache cả kết quả null để không đọc đĩa lại ở mỗi lần vẽ

        Bitmap? bitmap = null;
        try
        {
            var path = Path.Combine(FolderPath, file);
            if (File.Exists(path))
            {
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var source = Image.FromStream(stream);
                bitmap = new Bitmap(source); // bản sao độc lập với stream
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or OutOfMemoryException or ExternalException)
        {
            bitmap = null;
        }

        _images[file] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var bitmap in _images.Values)
            bitmap?.Dispose();
        _images.Clear();
    }
}