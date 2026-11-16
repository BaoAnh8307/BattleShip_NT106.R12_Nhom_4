using System.ComponentModel;
using System.Drawing.Drawing2D;
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
/// Control vẽ bàn cờ 10x10 bằng GDI+. Dùng cho bàn của mình (đặt tàu, hiện tàu, trúng/trượt do đối thủ bắn)
/// và bảng theo dõi (bắn vào đối thủ).
///
/// Cách dùng chính:
///  - Đặt tàu: Mode = Place; gán CurrentShip; nghe ShipPlaced; xoay bằng phím (RotationKey) hoặc gọi Rotate().
///  - Chiến đấu, bảng theo dõi: Mode = Target (hoặc Disabled khi chưa tới lượt); nghe CellClicked để gửi FIRE;
///    khi nhận FIRE_RESULT gọi ApplyResult(info).
///  - Chiến đấu, bàn của mình: LoadShips(...) rồi gọi ApplyResult(info) cho mỗi OPPONENT_FIRED.
/// Phần logic nằm trong <see cref="BoardModel"/> (đã có kiểm thử), lớp này chỉ vẽ và chuyển thao tác chuột.
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

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<ShipPlacement> PlacedShips => _model.Ships;

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

    /// <summary>Xóa cả tàu và dấu bắn, dùng khi bắt đầu ván mới.</summary>
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

    /// <summary>Ghi một phát bắn lên bàn cờ này.</summary>
    public void ApplyShot(int x, int y, ShotResult result, IReadOnlyList<Cell>? sunkCells = null)
    {
        _model.ApplyShot(x, y, result, sunkCells);
        Invalidate();
    }

    /// <summary>Tiện lợi: ghi kết quả từ FIRE_RESULT hoặc OPPONENT_FIRED.</summary>
    public void ApplyResult(FireResultInfo info) => ApplyShot(info.X, info.Y, info.Result, info.SunkCells);

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
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var label = _showCoordinates ? LabelSpace : 0;
        var layout = BoardGeometry.Compute(ClientSize.Width, ClientSize.Height, label);
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
        if (_showShips)
            DrawShips(g, ox, oy, cs);
        DrawMarks(g, ox, oy, cs);
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

    private void DrawShips(Graphics g, int ox, int oy, int cs)
    {
        using var fill = new SolidBrush(ShipColor);
        using var outline = new Pen(Color.FromArgb(95, 115, 132), 1.5f);

        foreach (var ship in _model.Ships)
        {
            var spec = Fleet.Find(ship.Name);
            if (spec is null) continue;

            var cellsX = ship.Dir == Direction.Horizontal ? spec.Length : 1;
            var cellsY = ship.Dir == Direction.Horizontal ? 1 : spec.Length;
            var rect = new Rectangle(ox + ship.X * cs + 3, oy + ship.Y * cs + 3, cellsX * cs - 6, cellsY * cs - 6);

            using var path = RoundedRect(rect, Math.Max(3, cs / 6));
            g.FillPath(fill, path);
            g.DrawPath(outline, path);
        }
    }

    private void DrawMarks(Graphics g, int ox, int oy, int cs)
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
                        using var path = RoundedRect(new Rectangle(px + 3, py + 3, cs - 6, cs - 6), Math.Max(3, cs / 6));
                        g.FillPath(sunkFill, path);
                        g.DrawPath(sunkOutline, path);
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
