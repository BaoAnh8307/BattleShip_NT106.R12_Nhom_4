using BattleShip.Network;

namespace BattleShip.Controls;

/// <summary>Dấu hiệu đã bắn trên một ô. Ô chưa bắn là <see cref="Empty"/>.</summary>
public enum CellMark
{
    Empty, // chưa bắn
    Hit,   // đã bắn trúng
    Miss,  // đã bắn trượt
    Sunk   // thuộc một tàu đã bị đánh chìm
}

/// <summary>
/// Vị trí và kích thước bàn cờ trong control: gốc (OriginX, OriginY) và độ lớn một ô (px).
/// </summary>
public readonly record struct BoardLayout(int OriginX, int OriginY, int CellSize)
{
    public int TotalSize => CellSize * BoardModel.Size;
}

/// <summary>Tính bố cục và đổi vị trí chuột thành ô. Thuần số học, không dùng WinForms.</summary>
public static class BoardGeometry
{
    /// <param name="labelSpace">Chỗ chừa cho nhãn tọa độ ở bên trên và bên trái (0 nếu không hiện nhãn).</param>
    public static BoardLayout Compute(int width, int height, int labelSpace = 0, int padding = 1)
    {
        var available = Math.Min(width, height) - labelSpace - 2 * padding;
        var cell = Math.Max(1, available / BoardModel.Size);
        var total = cell * BoardModel.Size;
        var originX = (width - labelSpace - total) / 2 + labelSpace;
        var originY = (height - labelSpace - total) / 2 + labelSpace;
        return new BoardLayout(originX, originY, cell);
    }

    public static bool TryHitTest(BoardLayout layout, int px, int py, out Cell cell)
    {
        cell = default;
        if (px < layout.OriginX || py < layout.OriginY)
            return false;

        var cx = (px - layout.OriginX) / layout.CellSize;
        var cy = (py - layout.OriginY) / layout.CellSize;
        if (cx >= BoardModel.Size || cy >= BoardModel.Size)
            return false;

        cell = new Cell(cx, cy);
        return true;
    }
}

/// <summary>
/// Trạng thái của một bàn cờ 10x10: các tàu đã đặt và các ô đã bắn.
/// Dùng cho cả bàn của mình (có tàu) và bảng theo dõi (chỉ có dấu bắn). Không phụ thuộc giao diện.
/// </summary>
public sealed class BoardModel
{
    public const int Size = Fleet.BoardSize;

    private readonly CellMark[,] _marks = new CellMark[Size, Size];
    private readonly List<ShipPlacement> _ships = [];

    public IReadOnlyList<ShipPlacement> Ships => _ships;

    /// <summary>Đã đặt đủ cả 5 tàu.</summary>
    public bool AllShipsPlaced => _ships.Count == Fleet.Ships.Count;

    public CellMark MarkAt(int x, int y)
    {
        if (!Fleet.InBounds(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), "Ô nằm ngoài bảng");
        return _marks[x, y];
    }

    /// <summary>Ô này đã bị bắn rồi (trúng, trượt hoặc thuộc tàu chìm).</summary>
    public bool AlreadyShot(int x, int y) => Fleet.InBounds(x, y) && _marks[x, y] != CellMark.Empty;

    // ----- Tàu -----

    /// <summary>Tên tàu đang chiếm ô này, hoặc null.</summary>
    public string? ShipNameAt(Cell cell)
    {
        foreach (var ship in _ships)
            if (Fleet.CellsOf(ship).Contains(cell))
                return ship.Name;
        return null;
    }

    /// <summary>
    /// Kiểm tra có đặt được tàu này không: tên hợp lệ, không ra ngoài bảng, không chồng lên tàu khác.
    /// Tàu cùng tên đang có trên bàn được bỏ qua (để đặt lại). Được phép đặt sát nhau.
    /// </summary>
    public bool CanPlace(ShipPlacement placement, out string? reason)
    {
        if (Fleet.Find(placement.Name) is null)
        {
            reason = $"Tên tàu không hợp lệ: {placement.Name}";
            return false;
        }

        var occupied = new HashSet<Cell>();
        foreach (var other in _ships.Where(s => s.Name != placement.Name))
            foreach (var cell in Fleet.CellsOf(other))
                occupied.Add(cell);

        foreach (var cell in Fleet.CellsOf(placement))
        {
            if (!Fleet.InBounds(cell.X, cell.Y))
            {
                reason = "Tàu nằm ngoài bảng";
                return false;
            }
            if (occupied.Contains(cell))
            {
                reason = "Tàu chồng lên tàu khác";
                return false;
            }
        }

        reason = null;
        return true;
    }

    /// <summary>Đặt tàu. Nếu đã có tàu cùng tên thì thay thế.</summary>
    public bool TryPlace(ShipPlacement placement, out string? reason)
    {
        if (!CanPlace(placement, out reason))
            return false;

        _ships.RemoveAll(s => s.Name == placement.Name);
        _ships.Add(placement);
        return true;
    }

    public bool RemoveShip(string name) => _ships.RemoveAll(s => s.Name == name) > 0;

    public void ClearShips() => _ships.Clear();

    /// <summary>Nạp sẵn một đội tàu (ví dụ chuyển từ màn đặt tàu sang màn chiến đấu). Tàu không hợp lệ bị bỏ qua.</summary>
    public void LoadShips(IEnumerable<ShipPlacement> ships)
    {
        _ships.Clear();
        foreach (var ship in ships)
            TryPlace(ship, out _);
    }

    /// <summary>Các ô tàu sẽ chiếm nếu đặt tại <paramref name="origin"/> (có thể gồm ô ngoài bảng).</summary>
    public IReadOnlyList<Cell> PreviewCells(Cell origin, ShipSpec spec, Direction dir) =>
        Fleet.CellsOf(new ShipPlacement(spec.Name, origin.X, origin.Y, dir));

    public bool IsValidPreview(Cell origin, ShipSpec spec, Direction dir) =>
        CanPlace(new ShipPlacement(spec.Name, origin.X, origin.Y, dir), out _);

    // ----- Phát bắn -----

    /// <summary>
    /// Ghi kết quả một phát bắn. Với <see cref="ShotResult.Sunk"/>, mọi ô trong <paramref name="sunkCells"/>
    /// chuyển thành <see cref="CellMark.Sunk"/>.
    /// </summary>
    public void ApplyShot(int x, int y, ShotResult result, IReadOnlyList<Cell>? sunkCells = null)
    {
        if (!Fleet.InBounds(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), "Ô nằm ngoài bảng");

        switch (result)
        {
            case ShotResult.Miss:
                _marks[x, y] = CellMark.Miss;
                break;
            case ShotResult.Hit:
                _marks[x, y] = CellMark.Hit;
                break;
            case ShotResult.Sunk:
                _marks[x, y] = CellMark.Sunk;
                if (sunkCells is not null)
                    foreach (var cell in sunkCells.Where(c => Fleet.InBounds(c.X, c.Y)))
                        _marks[cell.X, cell.Y] = CellMark.Sunk;
                break;
        }
    }

    public void ClearMarks()
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                _marks[x, y] = CellMark.Empty;
    }

    public void Reset()
    {
        ClearShips();
        ClearMarks();
    }
}
