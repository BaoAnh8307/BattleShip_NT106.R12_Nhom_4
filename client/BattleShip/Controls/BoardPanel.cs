using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BattleShip.Network;

namespace BattleShip.Controls;

/// <summary>Cách bàn cờ phản ứng với chuột.</summary>
public enum BoardMode
{
    /// <summary>Không nhận thao tác, hiện mờ đi (ví dụ chưa tới lượt).</summary>
    Disabled,
    /// <summary>Đặt tàu: rê chuột để xem trước, bấm để đặt <see cref="BoardPanel.CurrentShip"/>.</summary>
    Place,
    /// <summary>Bắn: bấm vào ô chưa bắn thì bắn <see cref="BoardPanel.CellClicked"/>.</summary>
    Target
}

/// <summary>
/// Control vẽ bàn cờ 10x10 bằng GDI+, dùng ảnh tàu (Assets\Ships) và tự lùi về khối màu nếu thiếu ảnh.
///  - Đặt tàu: Mode = Place; gán CurrentShip; nghe ShipPlaced; xoay bằng RotationKey hoặc Rotate().
///  - Chiến đấu, bảng theo dõi: ShowShips = false; Mode = Target hoặc Disabled; nghe CellClicked;
///    khi nhận FIRE_RESULT gọi ApplyResult(info) (tàu địch chỉ lộ ra khi đã chìm).
///  - Chiến đấu, bàn của mình: ShowShips = true; LoadShips(...); ApplyResult(info) cho mỗi OPPONENT_FIRED.
/// </summary>
public class BoardPanel : Control
{
    private const int LabelSpace = 18;

    private readonly BoardModel _model = new();
    private Cell? _hover;
    private BoardMode _mode = BoardMode.Disabled;
    private ShipSpec? _currentShip;
    private Direction _direction = Direction.Horizontal;
    private bool _showShips = true;
    private bool _showCoordinates;
    private bool _useShipImages = true;
    private ShipSpriteCache? _sprites = ShipSpriteCache.Shared;

    public BoardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        TabStop = true;
        DoubleBuffered = true;
        Size = new Size(340, 340);
    }

    // ----- Sự kiện -----

    /// <summary>Mode = Target: người chơi bấm vào một ô chưa bắn. Tham số là (x, y).</summary>
    public event Action<int, int>? CellClicked;

    /// <summary>Mode = Place: người chơi vừa đặt xong <see cref="CurrentShip"/>.</summary>
    public event Action<ShipPlacement>? ShipPlaced;

    // ----- Thuộc tính -----

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BoardMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            _hover = null;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    /// <summary>Tàu đang được đặt (Mode = Place). Null thì không xem trước được.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ShipSpec? CurrentShip
    {
        get => _currentShip;
        set
        {
            _currentShip = value;
            Invalidate();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Direction CurrentDirection
    {
        get => _direction;
        set
        {
            _direction = value;
            Invalidate();
        }
    }

    /// <summary>Phím xoay tàu khi bàn cờ đang có tiêu điểm (mặc định R). Tính năng bổ sung.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys RotationKey { get; set; } = Keys.R;

    /// <summary>true: vẽ các tàu đã nạp (bàn của mình). false: bảng địch, KHÔNG vẽ tàu chưa chìm.</summary>
    [DefaultValue(true)]
    public bool ShowShips
    {
        get => _showShips;
        set
        {
            _showShips = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool ShowCoordinates
    {
        get => _showCoordinates;
        set
        {
            _showCoordinates = value;
            Invalidate();
        }
    }

    /// <summary>false: luôn vẽ khối màu, không dùng ảnh tàu.</summary>
    [DefaultValue(true)]
    public bool UseShipImages
    {
        get => _useShipImages;
        set
        {
            _useShipImages = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Nơi lấy ảnh tàu (mặc định <see cref="ShipSpriteCache.Shared"/>). BoardPanel không sở hữu nên không Dispose nó;
    /// nếu bạn tự tạo một cache riêng thì tự Dispose khi đóng Form.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ShipSpriteCache? Sprites
    {
        get => _sprites;
        set
        {
            _sprites = value;
            Invalidate();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<ShipPlacement> PlacedShips => _model.Ships;

    /// <summary>Các tàu đã chìm và đã lộ ra nguyên con trên bàn này.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<ShipPlacement> SunkShips => _model.SunkShips;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllShipsPlaced => _model.AllShipsPlaced;

    // Màu sắc (đổi bằng code nếu muốn hợp với giao diện)
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color WaterColor { get; set; } = Color.FromArgb(18, 58, 94);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GridColor { get; set; } = Color.FromArgb(44, 90, 134);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ShipColor { get; set; } = Color.FromArgb(143, 163, 179);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HitColor { get; set; } = Color.FromArgb(229, 72, 77);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SunkColor { get; set; } = Color.FromArgb(142, 31, 38);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color MissColor { get; set; } = Color.FromArgb(220, 232, 245);

    // ----- Thao tác từ form -----

    /// <summary>Xoay hướng tàu đang đặt giữa ngang và dọc.</summary>
    public void Rotate()
    {
        _direction = _direction == Direction.Horizontal ? Direction.Vertical : Direction.Horizontal;
        Invalidate();
    }

    /// <summary>Xóa hết tàu đã đặt (nút Xóa hết).</summary>
    public void ClearShips()
    {
        _model.ClearShips();
        Invalidate();
    }

    /// <summary>Xóa cả tàu, dấu bắn và tàu chìm, dùng khi bắt đầu ván mới.</summary>
    public void Reset()
    {
        _model.Reset();
        _hover = null;
        Invalidate();
    }

    /// <summary>Nạp sẵn đội tàu để hiện trên bàn của mình (màn chiến đấu).</summary>
    public void LoadShips(IEnumerable<ShipPlacement> ships)
    {
        _model.LoadShips(ships);
        Invalidate();
    }

    /// <summary>Ghi một phát bắn. Truyền <paramref name="sunkShip"/> khi tàu chìm để hiện nguyên sprite con tàu.</summary>
    public void ApplyShot(int x, int y, ShotResult result, IReadOnlyList<Cell>? sunkCells = null, string? sunkShip = null)
    {
        _model.ApplyShot(x, y, result, sunkCells, sunkShip);
        Invalidate();
    }

    /// <summary>Tiện lợi: ghi kết quả từ FIRE_RESULT hoặc OPPONENT_FIRED (đã gồm tên tàu và các ô tàu chìm).</summary>
    public void ApplyResult(FireResultInfo info) =>
        ApplyShot(info.X, info.Y, info.Result, info.SunkCells, info.SunkShip);

    /// <summary>Lộ các tàu đã chìm khi đã biết đầy đủ vị trí. Trả về số tàu hợp lệ đã lộ.</summary>
    public int RevealSunkShips(IEnumerable<ShipPlacement> ships)
    {
        var count = ships.Count(ship => _model.RevealSunkShip(ship));
        Invalidate();
        return count;
    }

    public bool IsAlreadyShot(int x, int y) => _model.AlreadyShot(x, y);

    // ----- Chuột và bàn phím -----

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (_mode == BoardMode.Place)
            Focus(); // để phím xoay tàu hoạt động khi chuột đang ở trên bàn cờ
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        Cell? cell = _mode == BoardMode.Disabled ? null : HitTest(e.Location);
        var overShootable = _mode == BoardMode.Target && cell.HasValue && !_model.AlreadyShot(cell.Value.X, cell.Value.Y);
        Cursor = overShootable ? Cursors.Hand : Cursors.Default;

        if (cell != _hover)
        {
            _hover = cell;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || _mode == BoardMode.Disabled)
            return;

        var cell = HitTest(e.Location);
        if (cell is null)
            return;

        if (_mode == BoardMode.Target)
        {
            if (!_model.AlreadyShot(cell.Value.X, cell.Value.Y))
                CellClicked?.Invoke(cell.Value.X, cell.Value.Y);
        }
        else if (_mode == BoardMode.Place && _currentShip is not null)
        {
            var placement = new ShipPlacement(_currentShip.Name, cell.Value.X, cell.Value.Y, _direction);
            if (_model.TryPlace(placement, out _))
            {
                _currentShip = null;
                Invalidate();
                ShipPlaced?.Invoke(placement);
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_mode == BoardMode.Place && e.KeyCode == RotationKey)
        {
            Rotate();
            e.Handled = true;
        }
    }

    private Cell? HitTest(Point point)
    {
        var layout = BoardGeometry.Compute(ClientSize.Width, ClientSize.Height, _showCoordinates ? LabelSpace : 0);
        return BoardGeometry.TryHitTest(layout, point.X, point.Y, out var cell) ? cell : null;
    }

    // ----- Vẽ -----

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, ClientSize);
    }

    /// <summary>
    /// Vẽ toàn bộ bàn cờ vào <paramref name="g"/> với vùng <paramref name="size"/>. Tách riêng khỏi OnPaint
    /// để kiểm thử được bằng cách vẽ vào một Bitmap, không cần hiển thị form.
    /// </summary>
    public void Render(Graphics g, Size size)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(BackColor);

        var label = _showCoordinates ? LabelSpace : 0;
        var layout = BoardGeometry.Compute(size.Width, size.Height, label);
        var cs = layout.CellSize;
        var ox = layout.OriginX;
        var oy = layout.OriginY;
        var total = layout.TotalSize;
        var board = new Rectangle(ox, oy, total, total);

        using (var water = new SolidBrush(WaterColor))
            g.FillRectangle(water, board);

        using (var gridPen = new Pen(GridColor, 1f))
        {
            for (var i = 1; i < BoardModel.Size; i++)
            {
                g.DrawLine(gridPen, ox + i * cs, oy, ox + i * cs, oy + total);
                g.DrawLine(gridPen, ox, oy + i * cs, ox + total, oy + i * cs);
            }
        }

        if (_showCoordinates)
            DrawCoordinates(g, ox, oy, cs, label);

        // Các ô đã được phủ bằng ảnh tàu chìm: không vẽ thêm ô đỏ sẫm đè lên.
        var covered = new HashSet<Cell>();
        if (_showShips)
            DrawShips(g, ox, oy, cs, covered); // sương mù: bảng địch có ShowShips = false nên không bao giờ vẽ tàu chưa chìm
        DrawRevealedShips(g, ox, oy, cs, covered);
        DrawMarks(g, ox, oy, cs, covered);     // dấu trúng/trượt luôn nằm TRÊN ảnh tàu
        DrawHover(g, ox, oy, cs);

        if (_mode == BoardMode.Disabled)
        {
            using var dim = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            g.FillRectangle(dim, board);
        }

        using var border = new Pen(Color.FromArgb(52, 80, 107), 2f);
        g.DrawRectangle(border, board);
    }

    private void DrawCoordinates(Graphics g, int ox, int oy, int cs, int label)
    {
        using var brush = new SolidBrush(ForeColor);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (var i = 0; i < BoardModel.Size; i++)
        {
            g.DrawString(i.ToString(), Font, brush, new Rectangle(ox + i * cs, oy - label, cs, label), format);
            g.DrawString(i.ToString(), Font, brush, new Rectangle(ox - label, oy + i * cs, label, cs), format);
        }
    }

    private Image? GetSprite(string shipName, int length, Direction dir) =>
        _useShipImages ? _sprites?.Get(shipName, length, dir) : null;

    private static Rectangle ShipRectangle(int ox, int oy, int cs, int x, int y, int length, Direction dir) =>
        new(ox + x * cs, oy + y * cs,
            (dir == Direction.Horizontal ? length : 1) * cs,
            (dir == Direction.Horizontal ? 1 : length) * cs);

    /// <summary>Vẽ ảnh tàu trải liền trên hình chữ nhật <paramref name="dest"/>; sunk = làm tối và ngả đỏ.</summary>
    private static void DrawShipImage(Graphics g, Image image, Rectangle dest, float opacity, bool sunk)
    {
        var matrix = new ColorMatrix(new float[][]
        {
            [sunk ? 0.55f : 1f, 0, 0, 0, 0],
            [0, sunk ? 0.30f : 1f, 0, 0, 0],
            [0, 0, sunk ? 0.30f : 1f, 0, 0],
            [0, 0, 0, opacity, 0],
            [0, 0, 0, 0, 1],
        });

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        attributes.SetWrapMode(WrapMode.TileFlipXY); // tránh viền lem ở mép ảnh khi co giãn
        g.DrawImage(image, dest, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }

    private void DrawShips(Graphics g, int ox, int oy, int cs, HashSet<Cell> covered)
    {
        using var fill = new SolidBrush(ShipColor);
        using var outline = new Pen(Color.FromArgb(95, 115, 132), 1.5f);

        foreach (var ship in _model.Ships)
        {
            var spec = Fleet.Find(ship.Name);
            if (spec is null)
                continue;

            var rect = ShipRectangle(ox, oy, cs, ship.X, ship.Y, spec.Length, ship.Dir);
            var image = GetSprite(ship.Name, spec.Length, ship.Dir);
            if (image is not null)
            {
                var sunk = _model.IsShipSunk(ship);
                DrawShipImage(g, image, rect, 1f, sunk);
                if (sunk)
                    foreach (var cell in Fleet.CellsOf(ship))
                        covered.Add(cell);
            }
            else
            {
                // Không có ảnh: vẽ khối màu như trước.
                using var path = RoundedRect(Rectangle.Inflate(rect, -3, -3), Math.Max(3, cs / 6));
                g.FillPath(fill, path);
                g.DrawPath(outline, path);
            }
        }
    }

    /// <summary>Vẽ các tàu đã chìm và được lộ ra (chủ yếu cho bảng theo dõi của địch).</summary>
    private void DrawRevealedShips(Graphics g, int ox, int oy, int cs, HashSet<Cell> covered)
    {
        foreach (var revealed in _model.RevealedShips)
        {
            var placement = revealed.Placement;

            // Tàu của chính mình đã được DrawShips vẽ (kèm hiệu ứng chìm), không vẽ lại.
            if (_showShips && _model.Ships.Any(s => s.Name == placement.Name))
                continue;

            var image = GetSprite(placement.Name, revealed.Length, placement.Dir);
            if (image is null)
                continue; // thiếu ảnh: các ô chìm vẫn được DrawMarks tô đỏ sẫm

            var rect = ShipRectangle(ox, oy, cs, placement.X, placement.Y, revealed.Length, placement.Dir);
            DrawShipImage(g, image, rect, 1f, sunk: true);

            for (var i = 0; i < revealed.Length; i++)
                covered.Add(placement.Dir == Direction.Horizontal
                    ? new Cell(placement.X + i, placement.Y)
                    : new Cell(placement.X, placement.Y + i));
        }
    }

    private void DrawMarks(Graphics g, int ox, int oy, int cs, HashSet<Cell> covered)
    {
        using var hitFill = new SolidBrush(HitColor);
        using var hitOutline = new Pen(Color.White, 1.5f);
        using var sunkFill = new SolidBrush(SunkColor);
        using var sunkOutline = new Pen(HitColor, 1.5f);
        using var missFill = new SolidBrush(MissColor);

        for (var y = 0; y < BoardModel.Size; y++)
        {
            for (var x = 0; x < BoardModel.Size; x++)
            {
                var px = ox + x * cs;
                var py = oy + y * cs;
                switch (_model.MarkAt(x, y))
                {
                    case CellMark.Hit:
                        {
                            var r = cs * 0.28f;
                            g.FillEllipse(hitFill, px + cs / 2f - r, py + cs / 2f - r, 2 * r, 2 * r);
                            g.DrawEllipse(hitOutline, px + cs / 2f - r, py + cs / 2f - r, 2 * r, 2 * r);
                            break;
                        }
                    case CellMark.Miss:
                        {
                            var r = cs * 0.12f;
                            g.FillEllipse(missFill, px + cs / 2f - r, py + cs / 2f - r, 2 * r, 2 * r);
                            break;
                        }
                    case CellMark.Sunk:
                        {
                            if (!covered.Contains(new Cell(x, y)))
                            {
                                using var path = RoundedRect(new Rectangle(px + 3, py + 3, cs - 6, cs - 6), Math.Max(3, cs / 6));
                                g.FillPath(sunkFill, path);
                                g.DrawPath(sunkOutline, path);
                            }
                            break;
                        }
                }
                }
            }
        }

    private void DrawHover(Graphics g, int ox, int oy, int cs)
    {
        if (_hover is null)
            return;
        var hover = _hover.Value;

        if (_mode == BoardMode.Target)
        {
            if (_model.AlreadyShot(hover.X, hover.Y))
                return;
            using var brush = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            g.FillRectangle(brush, ox + hover.X * cs + 1, oy + hover.Y * cs + 1, cs - 1, cs - 1);
        }
        else if (_mode == BoardMode.Place && _currentShip is not null)
        {
            var valid = _model.IsValidPreview(hover, _currentShip, _direction);
            var image = valid ? GetSprite(_currentShip.Name, _currentShip.Length, _direction) : null;

            if (image is not null)
            {
                // Xem trước bằng chính ảnh tàu, bán trong suốt, kèm khung xanh.
                var rect = ShipRectangle(ox, oy, cs, hover.X, hover.Y, _currentShip.Length, _direction);
                DrawShipImage(g, image, rect, 0.65f, sunk: false);
                using var pen = new Pen(Color.FromArgb(200, 47, 180, 124), 2f);
                g.DrawRectangle(pen, rect);
                return;
            }

            using var brush = new SolidBrush(valid ? Color.FromArgb(160, 47, 180, 124) : Color.FromArgb(160, 229, 72, 77));
            foreach (var cell in _model.PreviewCells(hover, _currentShip, _direction))
            {
                if (!Fleet.InBounds(cell.X, cell.Y))
                    continue; // phần ra ngoài bảng thì không vẽ, ô còn lại đã đỏ
                g.FillRectangle(brush, ox + cell.X * cs + 2, oy + cell.Y * cs + 2, cs - 3, cs - 3);
            }
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        if (r < 1)
        {
            path.AddRectangle(rect);
            return path;
        }

        var d = r * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}