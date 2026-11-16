using BattleShip.App;
using BattleShip.Controls;
using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>
/// Màn đặt tàu (trạng thái PLACING). Mở sau khi nhận MATCH_FOUND, truyền <see cref="MatchFoundInfo"/> vào.
///
/// Kết quả đóng form (dùng <c>ShowDialog</c>):
///  - <see cref="DialogResult.OK"/>: cả hai đã sẵn sàng, đã nhận GAME_START. Mở FormBattle với
///    <see cref="Match"/> và <see cref="PlacedShips"/>.
///  - <see cref="DialogResult.Cancel"/>: về sảnh (đối thủ rời trận, người chơi rời trận, hoặc trận không còn).
///  - <see cref="DialogResult.Abort"/>: mất kết nối tới server (nên quay về màn đăng nhập).
/// </summary>
public class FormPlaceShips : Form
{
    private readonly MatchFoundInfo _match;
    private readonly BoardPanel _board = new() { Location = new Point(24, 84), Size = new Size(358, 358) };
    private readonly Dictionary<string, Button> _shipButtons = [];
    private readonly Button _clearButton = Theme.PrimaryButton("Xóa hết");
    private readonly Button _readyButton = Theme.PrimaryButton("Sẵn sàng");
    private readonly Label _status = Theme.Status();

    private ShipSpec? _selected;
    private IReadOnlyList<ShipPlacement> _placed = [];
    private bool _busy;
    private bool _leaving;

    public FormPlaceShips(MatchFoundInfo match)
    {
        _match = match;
        Text = "BattleShip - Đặt tàu";
        Theme.ApplyForm(this, 690, 540);
        KeyPreview = true;

        BuildLayout();

        _board.Mode = BoardMode.Place;
        _board.ShipPlaced += _ => OnShipPlaced();
        _clearButton.Click += (_, _) => ClearAll();
        _readyButton.Click += async (_, _) => await ReadyAsync();
        KeyDown += OnFormKeyDown;

        var client = AppSession.Client;
        client.OnGameStart += HandleGameStart;
        client.OnMatchCancelled += HandleMatchCancelled;
        client.OnDisconnected += HandleDisconnected;
    }

    /// <summary>Thông tin trận (đối thủ, ai bắn trước), truyền tiếp cho FormBattle.</summary>
    public MatchFoundInfo Match => _match;

    /// <summary>Các tàu đã đặt và gửi lên server. Chỉ có giá trị khi DialogResult là OK.</summary>
    public IReadOnlyList<ShipPlacement> PlacedShips => _placed;

    // ----- Dựng giao diện -----

    private void BuildLayout()
    {
        var header = new Label
        {
            Text = $"Đối thủ: {_match.Opponent}   ·   {(_match.YouFirst ? "Bạn bắn trước" : "Đối thủ bắn trước")}",
            Location = new Point(24, 18),
            Size = new Size(640, 30),
            AutoSize = false,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Theme.Text,
        };
        var boardLabel = new Label
        {
            Text = "Bàn cờ của bạn",
            Location = new Point(24, 58),
            Size = new Size(358, 22),
            AutoSize = false,
            ForeColor = Theme.Muted,
        };

        _status.Location = new Point(24, 452);
        _status.Size = new Size(358, 56);
        _status.TextAlign = ContentAlignment.MiddleLeft;

        var side = new FlowLayoutPanel
        {
            Location = new Point(410, 54),
            Size = new Size(256, 454),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Background,
        };

        var shipsLabel = Theme.FieldLabel("Danh sách tàu");
        shipsLabel.Width = 250;
        side.Controls.Add(shipsLabel);

        foreach (var spec in Fleet.Ships)
        {
            var button = CreateShipButton();
            var current = spec;
            button.Click += (_, _) =>
            {
                if (!_busy)
                    SelectShip(current);
            };
            _shipButtons[spec.Name] = button;
            side.Controls.Add(button);
        }

        var hint = new Label
        {
            Text = "Rê chuột vào bàn cờ rồi nhấn phím R để xoay tàu (ngang / dọc).",
            Width = 250,
            Height = 48,
            AutoSize = false,
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 4, 0, 10),
        };
        side.Controls.Add(hint);

        StyleSecondary(_clearButton);
        _clearButton.Width = 250;
        _clearButton.Height = 38;
        _clearButton.Margin = new Padding(0, 0, 0, 8);
        side.Controls.Add(_clearButton);

        _readyButton.Width = 250;
        _readyButton.Margin = new Padding(0);
        _readyButton.Enabled = false;
        side.Controls.Add(_readyButton);

        Controls.Add(header);
        Controls.Add(boardLabel);
        Controls.Add(_board);
        Controls.Add(_status);
        Controls.Add(side);
    }

    private static Button CreateShipButton()
    {
        var button = new Button
        {
            Width = 250,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Text,
            BackColor = Theme.Field,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10.5f),
            Padding = new Padding(10, 0, 0, 0),
            Margin = new Padding(0, 0, 0, 6),
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(43, 74, 109);
        return button;
    }

    private static void StyleSecondary(Button button)
    {
        button.BackColor = Color.FromArgb(38, 56, 75);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 74, 98);
    }

    // ----- Đặt tàu -----

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Trận có thể đã bị hủy trước khi form mở (ví dụ đối thủ rời ngay sau MATCH_FOUND).
        if (AppSession.Client.State != SessionState.Placing)
        {
            _leaving = true;
            MessageBox.Show(this, "Trận đấu không còn nữa (đối thủ có thể đã rời). Bạn sẽ quay về sảnh.",
                "BattleShip", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.Cancel;
            return;
        }

        SelectShip(PlacementPlan.NextUnplaced(_board.PlacedShips));
    }

    private void SelectShip(ShipSpec? spec)
    {
        _selected = spec;
        _board.CurrentShip = spec;
        RefreshShipList();

        if (spec is not null)
            Theme.ShowProgress(_status, $"Đặt: {spec.DisplayName} ({spec.Length} ô). Bấm vào bàn cờ để đặt, phím R để xoay.");
        else if (_board.AllShipsPlaced)
            Theme.ShowInfo(_status, "Đã đặt đủ 5 tàu. Bấm Sẵn sàng khi bạn muốn bắt đầu.");
    }

    private void OnShipPlaced()
    {
        // Sau khi đặt xong một tàu, tự chuyển sang tàu chưa đặt kế tiếp.
        SelectShip(PlacementPlan.NextUnplaced(_board.PlacedShips));
    }

    private void ClearAll()
    {
        if (_busy)
            return;

        _board.ClearShips();
        SelectShip(Fleet.Ships[0]);
    }

    private void RefreshShipList()
    {
        var placedNames = _board.PlacedShips.Select(s => s.Name).ToHashSet();
        foreach (var spec in Fleet.Ships)
        {
            var button = _shipButtons[spec.Name];
            button.Text = PlacementPlan.Label(spec, placedNames.Contains(spec.Name));

            var selected = _selected is not null && _selected.Name == spec.Name;
            button.BackColor = selected ? Color.FromArgb(30, 70, 112) : Theme.Field;
            button.FlatAppearance.BorderColor = selected ? Theme.Accent : Color.FromArgb(43, 74, 109);
        }
        _readyButton.Enabled = _board.AllShipsPlaced && !_busy;
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        // Phím xoay hoạt động cả khi bàn cờ chưa có tiêu điểm.
        if (e.KeyCode == _board.RotationKey && !_busy)
        {
            _board.Rotate();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    // ----- Sẵn sàng -----

    private async Task ReadyAsync()
    {
        if (_busy || !_board.AllShipsPlaced)
            return;

        SetBusy(true);
        Theme.ShowProgress(_status, "Đang gửi vị trí tàu...");
        try
        {
            // Lưu trước khi gửi: GAME_START có thể đến ngay khi READY được xử lý.
            var ships = _board.PlacedShips.ToList();
            _placed = ships;

            await AppSession.Client.PlaceShipsAsync(ships);
            await AppSession.Client.ReadyAsync();

            if (IsDisposed)
                return;
            _board.Mode = BoardMode.Disabled;
            Theme.ShowProgress(_status, "Đã sẵn sàng. Đang chờ đối thủ...");
            // Giữ trạng thái bận: không cho sửa nữa. HandleGameStart sẽ đóng form.
        }
        catch (Exception ex)
        {
            if (IsDisposed)
                return;
            Theme.ShowError(_status, ErrorTexts.FromException(ex, AppSession.Config));
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _clearButton.Enabled = !busy;
        foreach (var button in _shipButtons.Values)
            button.Enabled = !busy;
        _board.Mode = busy ? BoardMode.Disabled : BoardMode.Place;
        _board.CurrentShip = busy ? null : _selected;
        _readyButton.Enabled = !busy && _board.AllShipsPlaced;
    }

    // ----- Sự kiện từ server -----

    private void HandleGameStart() => RunOnUi(() => DialogResult = DialogResult.OK);

    private void HandleMatchCancelled(string reason) => RunOnUi(() =>
    {
        if (_leaving)
            return;
        _leaving = true;
        MessageBox.Show(this, "Đối thủ đã rời trận. Bạn sẽ quay về sảnh.",
            "BattleShip", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.Cancel;
    });

    private void HandleDisconnected(string reason) => RunOnUi(() =>
    {
        if (_leaving)
            return;
        _leaving = true;
        MessageBox.Show(this, reason, "BattleShip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        DialogResult = DialogResult.Abort;
    });

    private void RunOnUi(Action action)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }

    // ----- Đóng form -----

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _leaving || DialogResult != DialogResult.None)
            return;

        // Người chơi bấm dấu X khi trận chưa bắt đầu: coi như rời trận.
        var state = AppSession.Client.State;
        if (state is not (SessionState.Placing or SessionState.Ready))
            return;

        var answer = MessageBox.Show(this, "Rời trận? Đối thủ sẽ được thông báo.",
            "BattleShip", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _leaving = true;
        _ = LeaveMatchAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        var client = AppSession.Client;
        client.OnGameStart -= HandleGameStart;
        client.OnMatchCancelled -= HandleMatchCancelled;
        client.OnDisconnected -= HandleDisconnected;
        base.OnFormClosed(e);
    }

    private static async Task LeaveMatchAsync()
    {
        try
        {
            await AppSession.Client.SurrenderAsync(); // khi chưa bắt đầu, SURRENDER nghĩa là rời trận
        }
        catch (Exception)
        {
            // Đang mất kết nối thì thôi, server sẽ tự dọn.
        }
    }
}
