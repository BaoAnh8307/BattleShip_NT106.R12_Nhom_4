using System.Drawing.Drawing2D;
using BattleShip.Controls;
using BattleShip.Network;

namespace BattleShip.Forms;

public class FormBattle : Form
{
    // ----- Controls -----
    private readonly BoardPanel _myBoard;
    private readonly BoardPanel _enemyBoard;
    private readonly Label _lblTitle;
    private readonly Label _lblStatus;
    private readonly Label _lblTimer;
    private readonly Label _lblMyPlayerName;
    private readonly Label _lblEnemyPlayerName;
    private readonly Label _lblMyFleetStatus;
    private readonly Label _lblEnemyFleetStatus;
    private readonly RichTextBox _rtbLog;
    private readonly Button _btnSurrender;
    private readonly Button _btnLeave;
    private readonly System.Windows.Forms.Timer _turnTimer;

    // ----- Game State -----
    public bool IsMyTurn { get; private set; }
    public bool IsGameOver { get; private set; }
    public int MyRemainingShips { get; private set; } = 5;
    public int EnemyRemainingShips { get; private set; } = 5;
    public int TurnCountdown { get; private set; } = 30;
    /// <summary>Khi bật SilentMode (ví dụ trong unit test), không hiện MessageBox chặn luồng.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool SilentMode { get; set; } = false;

    // ----- Events kết nối với tầng mạng (Socket / Client) -----
    /// <summary>Kích hoạt khi người chơi nhấp chọn ô để bắn (x: 0..9, y: 0..9).</summary>
    public event Action<int, int>? FireRequested;

    /// <summary>Kích hoạt khi người chơi xác nhận đầu hàng.</summary>
    public event Action? SurrenderRequested;

    /// <summary>Kích hoạt khi người chơi bấm rời trận sau khi ván đấu kết thúc.</summary>
    public event Action? LeaveRequested;

    // Expose controls for testing/inspection
    public BoardPanel MyBoard => _myBoard;
    public BoardPanel EnemyBoard => _enemyBoard;
    public Label StatusLabel => _lblStatus;
    public RichTextBox BattleLog => _rtbLog;

    public FormBattle(
        IEnumerable<ShipPlacement> myPlacedShips,
        string playerName = "Bạn",
        string opponentName = "Đối thủ")
    {
        // 1. Áp dụng chuẩn Theme chung
        Theme.ApplyForm(this, 980, 700);
        Text = $"BattleShip - Trận chiến giữa [{playerName}] và [{opponentName}]";

        // 2. Tiêu đề
        _lblTitle = new Label
        {
            Text = "HẢI CHIẾN BẮN TÀU",
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(0, 10, ClientSize.Width, 38)
        };
        Controls.Add(_lblTitle);

        // 3. Thanh trạng thái trận đấu
        _lblStatus = new Label
        {
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(120, 50, ClientSize.Width - 240, 32),
            BackColor = Theme.Field
        };
        _lblStatus.Paint += (s, e) =>
        {
            using var borderPen = new Pen(Color.FromArgb(40, 70, 105), 1.5f);
            e.Graphics.DrawRectangle(borderPen, 0, 0, _lblStatus.Width - 1, _lblStatus.Height - 1);
        };
        Controls.Add(_lblStatus);

        // 4. Đồng hồ đếm ngược lượt đi
        _lblTimer = new Label
        {
            Text = "30s",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.Gold,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(ClientSize.Width - 110, 50, 80, 32),
            BackColor = Theme.Field
        };
        _lblTimer.Paint += (s, e) =>
        {
            using var borderPen = new Pen(Color.FromArgb(40, 70, 105), 1.5f);
            e.Graphics.DrawRectangle(borderPen, 0, 0, _lblTimer.Width - 1, _lblTimer.Height - 1);
        };
        Controls.Add(_lblTimer);

        // 5. Thẻ người chơi bên trái (Bạn)
        var myHeaderPanel = CreatePlayerHeader(50, 95, 410, playerName, isMe: true, out _lblMyPlayerName, out _lblMyFleetStatus);
        Controls.Add(myHeaderPanel);

        // 6. Thẻ người chơi bên phải (Đối thủ)
        var enemyHeaderPanel = CreatePlayerHeader(520, 95, 410, opponentName, isMe: false, out _lblEnemyPlayerName, out _lblEnemyFleetStatus);
        Controls.Add(enemyHeaderPanel);

        // 7. Bàn cờ của mình (My Board)
        _myBoard = new BoardPanel
        {
            Bounds = new Rectangle(50, 145, 410, 360),
            ShowShips = true,           // Luôn thấy tàu của mình
            ShowCoordinates = true,     // Hiện tọa độ 0..9
            Mode = BoardMode.Disabled,  // Không cho tự click bắn lên bàn mình
            BackColor = Theme.Field
        };
        _myBoard.LoadShips(myPlacedShips);
        Controls.Add(_myBoard);

        // 8. Bàn cờ đối thủ / Bảng ngắm bắn (Enemy Board)
        _enemyBoard = new BoardPanel
        {
            Bounds = new Rectangle(520, 145, 410, 360),
            ShowShips = false,          // Ẩn tàu đối thủ
            ShowCoordinates = true,     // Hiện tọa độ 0..9
            Mode = BoardMode.Disabled,  // Chưa tới lượt thì khóa
            BackColor = Theme.Field
        };
        _enemyBoard.CellClicked += OnEnemyCellClicked;
        Controls.Add(_enemyBoard);

        // 9. Nhật ký trận đấu (Battle Log)
        var lblLogTitle = new Label
        {
            Text = "📜 LỊCH SỬ BẮN:",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            Bounds = new Rectangle(50, 515, 200, 20)
        };
        Controls.Add(lblLogTitle);

        _rtbLog = new RichTextBox
        {
            Bounds = new Rectangle(50, 540, 680, 100),
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Font = new Font("Consolas", 10f)
        };
        Controls.Add(_rtbLog);

        // 10. Nút Đầu hàng & Rời trận
        _btnSurrender = new Button
        {
            Text = "🏳 Đầu Hàng",
            Bounds = new Rectangle(750, 540, 180, 44),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(160, 45, 55),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnSurrender.FlatAppearance.BorderSize = 0;
        _btnSurrender.Click += OnSurrenderClicked;
        Controls.Add(_btnSurrender);

        _btnLeave = new Button
        {
            Text = "🚪 Rời Trận",
            Bounds = new Rectangle(750, 596, 180, 44),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 75, 95),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Enabled = false // Chỉ sáng khi trận đấu kết thúc
        };
        _btnLeave.FlatAppearance.BorderSize = 0;
        _btnLeave.Click += (s, e) => LeaveRequested?.Invoke();
        Controls.Add(_btnLeave);

        // 11. Timer đếm ngược lượt
        _turnTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _turnTimer.Tick += OnTurnTimerTick;

        AddLog("Trận đấu bắt đầu! Hãy quan sát hạm đội và sẵn sàng chiến đấu.", Theme.Accent);
    }

    private static Panel CreatePlayerHeader(
        int x, int y, int width, string name, bool isMe,
        out Label lblName, out Label lblFleetStatus)
    {
        var panel = new Panel
        {
            Bounds = new Rectangle(x, y, width, 44),
            BackColor = Theme.Field
        };

        lblName = new Label
        {
            Text = isMe ? $"🛡 BẠN: {name}" : $"🎯 ĐỐI THỦ: {name}",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = isMe ? Theme.Accent : Theme.Error,
            Bounds = new Rectangle(10, 10, 230, 24),
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(lblName);

        lblFleetStatus = new Label
        {
            Text = "Còn: 5/5 tàu",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Theme.Success,
            Bounds = new Rectangle(240, 10, width - 250, 24),
            TextAlign = ContentAlignment.MiddleRight
        };
        panel.Controls.Add(lblFleetStatus);

        return panel;
    }

    // ----- Xử lý thao tác chuột & Nút bấm -----

    private void OnEnemyCellClicked(int x, int y)
    {
        if (!IsMyTurn || IsGameOver) return;

        // Khóa tạm thời bảng ngắm bắn để chống click liên tục trong khi chờ kết quả
        _enemyBoard.Mode = BoardMode.Disabled;
        Theme.ShowProgress(_lblStatus, $"Đang bắn vào tọa độ [{ToCoordString(x, y)}]...");
        AddLog($"[BẠN] Khai hỏa vào tọa độ [{ToCoordString(x, y)}]...", Theme.Text);

        // Kích hoạt sự kiện để bên Network/Socket gửi gói tin FIRE x y lên server
        FireRequested?.Invoke(x, y);
    }

    private void OnSurrenderClicked(object? sender, EventArgs e)
    {
        if (IsGameOver) return;

        var confirm = MessageBox.Show(
            "Bạn có chắc chắn muốn đầu hàng không? Trận đấu sẽ tính là bạn thua.",
            "Xác nhận đầu hàng",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm == DialogResult.Yes)
        {
            SurrenderRequested?.Invoke();
            EndGame(isWinner: false, "Bạn đã đầu hàng.");
        }
    }

    private void OnTurnTimerTick(object? sender, EventArgs e)
    {
        if (IsGameOver) return;

        TurnCountdown--;
        _lblTimer.Text = $"{TurnCountdown}s";

        if (TurnCountdown <= 5)
        {
            _lblTimer.ForeColor = Theme.Error;
        }

        if (TurnCountdown <= 0)
        {
            _turnTimer.Stop();
            AddLog("Hết thời gian lượt!", Theme.Error);
        }
    }

    // ----- Các phương thức gọi từ tầng Mạng (Thread-safe) -----

    /// <summary>
    /// Chuyển lượt đi. Tự động bật/tắt quyền ngắm bắn và đặt lại đồng hồ 30s.
    /// </summary>
    public void SetTurn(bool isMyTurn)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetTurn(isMyTurn));
            return;
        }

        if (IsGameOver) return;

        IsMyTurn = isMyTurn;
        TurnCountdown = 30;
        _lblTimer.Text = $"{TurnCountdown}s";
        _lblTimer.ForeColor = Color.Gold;
        _turnTimer.Stop();
        _turnTimer.Start();

        if (isMyTurn)
        {
            _enemyBoard.Mode = BoardMode.Target; // Bật quyền click ngắm bắn
            Theme.ShowInfo(_lblStatus, "🎯 LƯỢT CỦA BẠN: Nhấp vào ô trên bàn đối thủ để khai hỏa!");
            AddLog("--- Lượt của bạn ---", Theme.Accent);
        }
        else
        {
            _enemyBoard.Mode = BoardMode.Disabled; // Khóa bàn bắn
            Theme.ShowProgress(_lblStatus, "⏳ LƯỢT ĐỐI THỦ: Đang chờ đối phương khai hỏa...");
            AddLog("--- Lượt của đối thủ ---", Theme.Muted);
        }
    }

    /// <summary>
    /// Ghi nhận kết quả phát bắn của mình lên bàn đối thủ (từ gói tin FIRE_RESULT của Server).
    /// </summary>
    public void ApplyMyShotResult(FireResultInfo result)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyMyShotResult(result));
            return;
        }

        _enemyBoard.ApplyResult(result);
        var coord = ToCoordString(result.X, result.Y);

        switch (result.Result)
        {
            case ShotResult.Miss:
                AddLog($"[BẠN] Bắn vào [{coord}] -> 🌊 TRƯỢT!", Theme.Muted);
                Theme.ShowProgress(_lblStatus, $"Bạn đã bắn trượt ô [{coord}].");
                break;

            case ShotResult.Hit:
                AddLog($"[BẠN] Bắn vào [{coord}] -> 💥 TRÚNG TÀU ĐỐI PHƯƠNG!", Theme.Error);
                Theme.ShowInfo(_lblStatus, $"🔥 BẮN TRÚNG ô [{coord}]!");
                break;

            case ShotResult.Sunk:
                EnemyRemainingShips = Math.Max(0, EnemyRemainingShips - 1);
                _lblEnemyFleetStatus.Text = $"Còn: {EnemyRemainingShips}/5 tàu";
                AddLog($"[BẠN] Bắn vào [{coord}] -> ☠ ĐÃ ĐÁNH CHÌM MỘT TÀU ĐỊCH!", Color.FromArgb(255, 107, 107));
                Theme.ShowInfo(_lblStatus, $"💥 XUẤT SẮC! Đã đánh chìm tàu địch tại [{coord}]!");

                if (EnemyRemainingShips == 0)
                {
                    EndGame(isWinner: true, "Toàn bộ hạm đội đối thủ đã bị đánh chìm!");
                }
                break;
        }
    }

    /// <summary>
    /// Ghi nhận phát bắn của đối thủ lên bàn cờ của mình (từ gói tin OPPONENT_FIRED của Server).
    /// </summary>
    public void ApplyOpponentShot(FireResultInfo result)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyOpponentShot(result));
            return;
        }

        _myBoard.ApplyResult(result);
        var coord = ToCoordString(result.X, result.Y);

        switch (result.Result)
        {
            case ShotResult.Miss:
                AddLog($"[ĐỐI THỦ] Bắn vào [{coord}] -> 🌊 Trượt!", Theme.Success);
                break;

            case ShotResult.Hit:
                AddLog($"[ĐỐI THỦ] Bắn vào [{coord}] -> 💥 Tàu của bạn đã bị bắn trúng!", Theme.Error);
                Theme.ShowError(_lblStatus, $"Cảnh báo! Đối thủ đã bắn trúng tàu tại [{coord}]!");
                break;

            case ShotResult.Sunk:
                MyRemainingShips = Math.Max(0, MyRemainingShips - 1);
                _lblMyFleetStatus.Text = $"Còn: {MyRemainingShips}/5 tàu";
                _lblMyFleetStatus.ForeColor = MyRemainingShips <= 2 ? Theme.Error : Theme.Success;
                AddLog($"[ĐỐI THỦ] Bắn vào [{coord}] -> ☠ Một con tàu của bạn đã bị đánh chìm!", Theme.Error);
                Theme.ShowError(_lblStatus, $"Một con tàu của bạn đã chìm!");

                if (MyRemainingShips == 0)
                {
                    EndGame(isWinner: false, "Toàn bộ hạm đội của bạn đã bị đánh chìm!");
                }
                break;
        }
    }

    /// <summary>
    /// Kết thúc trận đấu: hiển thị bảng kết quả và kích hoạt nút rời trận.
    /// </summary>
    public void EndGame(bool isWinner, string reason)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => EndGame(isWinner, reason));
            return;
        }

        if (IsGameOver) return;

        IsGameOver = true;
        _turnTimer.Stop();
        _enemyBoard.Mode = BoardMode.Disabled;
        _btnSurrender.Enabled = false;
        _btnLeave.Enabled = true;

        if (isWinner)
        {
            _lblStatus.ForeColor = Theme.Success;
            _lblStatus.Text = "🏆 CHIẾN THẮNG! " + reason;
            AddLog($"=== CHIẾN THẮNG: {reason} ===", Theme.Success);
            if (!SilentMode)
                MessageBox.Show(reason, "🎉 Chúc mừng chiến thắng!", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _lblStatus.ForeColor = Theme.Error;
            _lblStatus.Text = "💥 THẤT BẠI! " + reason;
            AddLog($"=== THẤT BẠI: {reason} ===", Theme.Error);
            if (!SilentMode)
                MessageBox.Show(reason, "😢 Rất tiếc!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Ghi thông tin có màu vào ô nhật ký trận đấu.
    /// </summary>
    public void AddLog(string message, Color color)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AddLog(message, color));
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _rtbLog.SelectionStart = _rtbLog.TextLength;
        _rtbLog.SelectionLength = 0;
        _rtbLog.SelectionColor = Color.FromArgb(120, 140, 160);
        _rtbLog.AppendText($"[{timestamp}] ");

        _rtbLog.SelectionColor = color;
        _rtbLog.AppendText(message + Environment.NewLine);
        _rtbLog.ScrollToCaret();
    }

    /// <summary>Đổi tọa độ x=0..9, y=0..9 thành dạng cờ chuẩn ví dụ (A0, B4, C7,...).</summary>
    private static string ToCoordString(int x, int y)
    {
        char colChar = (char)('A' + x);
        return $"{colChar}{y}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _turnTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
