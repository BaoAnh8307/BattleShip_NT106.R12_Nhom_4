namespace BattleShip.Network;

/// <summary>
/// Danh sách tàu và quy tắc đặt tàu (mục 3.2 và 3.3 của PROTOCOL.md).
/// GUI dùng để kiểm tra nhanh trước khi gửi; server vẫn kiểm tra lại.
/// </summary>
public static class Fleet
{
    public const int BoardSize = 10;

    public static readonly IReadOnlyList<ShipSpec> Ships =
    [
        new("CARRIER", 5, "Tàu sân bay"),
        new("BATTLESHIP", 4, "Thiết giáp hạm"),
        new("CRUISER", 3, "Tàu tuần dương"),
        new("SUBMARINE", 3, "Tàu ngầm"),
        new("DESTROYER", 2, "Tàu khu trục"),
    ];

    public static ShipSpec? Find(string name) =>
        Ships.FirstOrDefault(s => s.Name == name);

    public static bool InBounds(int x, int y) =>
        x >= 0 && x < BoardSize && y >= 0 && y < BoardSize;

    /// <summary>Các ô tàu chiếm. Có thể nằm ngoài bảng, hãy dùng <see cref="InBounds"/> để kiểm tra.</summary>
    public static IReadOnlyList<Cell> CellsOf(ShipPlacement placement)
    {
        var spec = Find(placement.Name)
            ?? throw new ArgumentException($"Tên tàu không hợp lệ: {placement.Name}", nameof(placement));

        var cells = new List<Cell>(spec.Length);
        for (var i = 0; i < spec.Length; i++)
        {
            cells.Add(placement.Dir == Direction.Horizontal
                ? new Cell(placement.X + i, placement.Y)
                : new Cell(placement.X, placement.Y + i));
        }
        return cells;
    }

    /// <summary>
    /// Kiểm tra cả đội tàu. Trả về null nếu hợp lệ, ngược lại trả câu mô tả lỗi (tiếng Việt).
    /// Luật: đủ 5 tàu và mỗi tên một lần, không ra ngoài bảng, không chồng nhau.
    /// Tàu được phép đặt sát nhau.
    /// </summary>
    public static string? Validate(IReadOnlyList<ShipPlacement>? ships)
    {
        if (ships is null || ships.Count != Ships.Count)
            return $"Cần đặt đúng {Ships.Count} tàu";

        var names = new HashSet<string>();
        var occupied = new Dictionary<Cell, string>();

        foreach (var ship in ships)
        {
            if (Find(ship.Name) is null)
                return $"Tên tàu không hợp lệ: {ship.Name}";
            if (!names.Add(ship.Name))
                return $"Tàu {ship.Name} bị đặt trùng tên";

            foreach (var cell in CellsOf(ship))
            {
                if (!InBounds(cell.X, cell.Y))
                    return $"Tàu {ship.Name} nằm ngoài bảng";
                if (occupied.TryGetValue(cell, out var other))
                    return $"Tàu {ship.Name} chồng lên tàu {other}";
                occupied[cell] = ship.Name;
            }
        }
        return null;
    }
}
