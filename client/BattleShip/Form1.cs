using BattleShip.Controls;
using BattleShip.Network;

namespace BattleShip;

/// <summary>
/// Form thử BoardPanel (chỉ để xem bàn cờ hoạt động, KHÔNG đưa lên repo).
/// Bên trái: đặt 5 tàu lần lượt (rê chuột xem trước, bấm để đặt, phím R để xoay).
/// Bên phải: sau khi đặt đủ tàu, bấm vào bảng theo dõi để bắn thử; kết quả và nước đi của đối thủ là giả lập.
/// </summary>
public partial class Form1 : Form
{
    private readonly BoardPanel _myBoard = new()
    {
        Location = new Point(20, 80),
        Size = new Size(358, 358),
        ShowCoordinates = true,
    };
    private readonly BoardPanel _targetBoard = new()
    {
        Location = new Point(400, 80),
        Size = new Size(358, 358),
        ShowCoordinates = true,
        ShowShips = false,
    };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Random _rng = new();
    private int _nextShip;
    private int _shots;

    public Form1()
    {
        InitializeComponent();
        Text = "Thử BoardPanel";
        ClientSize = new Size(780, 500);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4) };
        AddButton(bar, "Xoay tàu (hoặc phím R)", () => _myBoard.Rotate());
        AddButton(bar, "Xóa hết, đặt lại", ResetAll);

        var leftTitle = new Label { Text = "Bàn của bạn", Location = new Point(20, 52), AutoSize = true };
        var rightTitle = new Label { Text = "Bảng theo dõi (bấm để bắn)", Location = new Point(400, 52), AutoSize = true };

        Controls.Add(_myBoard);
        Controls.Add(_targetBoard);
        Controls.Add(leftTitle);
        Controls.Add(rightTitle);
        Controls.Add(_status);
        Controls.Add(bar);

        _myBoard.ShipPlaced += _ =>
        {
            _nextShip++;
            if (_nextShip < Fleet.Ships.Count)
            {
                _myBoard.CurrentShip = Fleet.Ships[_nextShip];
                ShowPlacingStatus();
            }
            else
            {
                _myBoard.Mode = BoardMode.Disabled;
                _targetBoard.Mode = BoardMode.Target;
                _status.Text = "Đã đặt đủ 5 tàu. Bấm vào bảng theo dõi để bắn thử.";
            }
        };

        _targetBoard.CellClicked += OnTargetClicked;
        ResetAll();
    }

    private void ResetAll()
    {
        _myBoard.Reset();
        _targetBoard.Reset();
        _nextShip = 0;
        _shots = 0;
        _myBoard.Mode = BoardMode.Place;
        _myBoard.CurrentShip = Fleet.Ships[0];
        _myBoard.CurrentDirection = Direction.Horizontal;
        _targetBoard.Mode = BoardMode.Disabled;
        ShowPlacingStatus();
    }

    private void ShowPlacingStatus() =>
        _status.Text = $"Đang đặt: {Fleet.Ships[_nextShip].DisplayName} ({Fleet.Ships[_nextShip].Length} ô). Phím R để xoay.";

    private void OnTargetClicked(int x, int y)
    {
        _shots++;

        // Giả lập kết quả: mỗi 6 phát thì chìm một tàu, còn lại trúng hoặc trượt ngẫu nhiên.
        ShotResult result;
        IReadOnlyList<Cell>? sunk = null;
        if (_shots % 6 == 0)
        {
            result = ShotResult.Sunk;
            sunk = Fleet.InBounds(x + 1, y) ? [new Cell(x, y), new Cell(x + 1, y)] : [new Cell(x, y)];
        }
        else
        {
            result = _rng.Next(3) == 0 ? ShotResult.Hit : ShotResult.Miss;
        }
        _targetBoard.ApplyShot(x, y, result, sunk);

        // Giả lập đối thủ bắn lại vào bàn của mình.
        int ox, oy;
        do
        {
            ox = _rng.Next(Fleet.BoardSize);
            oy = _rng.Next(Fleet.BoardSize);
        } while (_myBoard.IsAlreadyShot(ox, oy));

        var onShip = _myBoard.PlacedShips.Any(s => Fleet.CellsOf(s).Contains(new Cell(ox, oy)));
        _myBoard.ApplyShot(ox, oy, onShip ? ShotResult.Hit : ShotResult.Miss);

        _status.Text = $"Bạn bắn ({x},{y}): {result}. Đối thủ bắn ({ox},{oy}): {(onShip ? "trúng" : "trượt")}.";
    }

    private static void AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }
}
