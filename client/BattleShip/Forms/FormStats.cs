using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>
/// Form Thống kê và lịch sử (mục 5.6 của báo cáo). Mở từ sảnh bằng ShowDialog.
/// Khi hiện lên, form gửi GET_STATS rồi GET_HISTORY qua <see cref="AppSession.Client"/>,
/// hiển thị ba ô (Đã chơi, Thắng, Thua) và bảng lịch sử các ván, mới nhất trước.
/// Nút "Quay lại sảnh" (hoặc phím Esc) đóng form.
/// </summary>
public sealed class FormStats : Form
{
    /// <summary>Số ván gần nhất cần lấy. Server cho tối đa 50, mặc định 20.</summary>
    private const int HistoryLimit = 20;

    private readonly INetworkClient _client = AppSession.Client;
    private readonly Label _played = StatBox("Đã chơi: ...");
    private readonly Label _wins = StatBox("Thắng: ...");
    private readonly Label _losses = StatBox("Thua: ...");
    private readonly DataGridView _grid = new();
    private readonly Label _status = Theme.Status();
    private readonly Button _back = Theme.PrimaryButton("Quay lại sảnh");

    public FormStats()
    {
        Text = "FormStats - Thống kê và lịch sử";
        Theme.ApplyForm(this, 640, 520);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // tiêu đề "Thống kê"
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));   // ba ô thống kê
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // tiêu đề "Lịch sử các ván"
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // bảng lịch sử
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // dòng trạng thái
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));   // nút quay lại

        var boxes = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        for (int i = 0; i < 3; i++)
            boxes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        boxes.Controls.Add(_played, 0, 0);
        boxes.Controls.Add(_wins, 1, 0);
        boxes.Controls.Add(_losses, 2, 0);

        SetupGrid();

        _status.Dock = DockStyle.Fill;
        _back.Anchor = AnchorStyles.None;
        _back.Width = 200;

        layout.Controls.Add(SectionLabel("Thống kê"), 0, 0);
        layout.Controls.Add(boxes, 0, 1);
        layout.Controls.Add(SectionLabel("Lịch sử các ván"), 0, 2);
        layout.Controls.Add(_grid, 0, 3);
        layout.Controls.Add(_status, 0, 4);
        layout.Controls.Add(_back, 0, 5);
        Controls.Add(layout);

        CancelButton = _back; // Esc = Quay lại sảnh
        _back.Click += (_, _) => Close();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Theme.ShowProgress(_status, "Đang tải dữ liệu…");
        try
        {
            // Gửi lần lượt từng lệnh, không gửi song song.
            var stats = await _client.GetStatsAsync();
            var history = await _client.GetHistoryAsync(HistoryLimit);
            if (IsDisposed)
                return;

            ShowStats(stats);
            ShowHistory(history);
            if (history.Count == 0)
                Theme.ShowProgress(_status, "Bạn chưa chơi ván nào.");
            else
                _status.Text = string.Empty;
        }
        catch (Exception ex)
        {
            if (IsDisposed)
                return;
            Theme.ShowError(_status, ErrorTexts.FromException(ex, AppSession.Config));
        }
    }

    private void ShowStats(StatsInfo stats)
    {
        _played.Text = StatsTexts.PlayedText(stats);
        _wins.Text = StatsTexts.WinsText(stats);
        _losses.Text = StatsTexts.LossesText(stats);
    }

    private void ShowHistory(IReadOnlyList<HistoryEntry> history)
    {
        _grid.Rows.Clear();
        foreach (var entry in history)
        {
            int row = _grid.Rows.Add(
                entry.Opponent,
                StatsTexts.ResultText(entry.Result),
                entry.TotalShots,
                StatsTexts.TimeText(entry.StartedAt));

            // Thắng màu xanh, thua màu đỏ cho dễ nhìn.
            _grid.Rows[row].Cells[1].Style.ForeColor =
                entry.Result == GameResult.Win ? Theme.Success : Theme.Error;
        }
        _grid.ClearSelection();
    }

    private void SetupGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BorderStyle = BorderStyle.None;
        _grid.BackgroundColor = Theme.Field;
        _grid.GridColor = Color.FromArgb(30, 56, 84);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 34;
        _grid.RowTemplate.Height = 30;

        var header = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(24, 56, 90),
            ForeColor = Theme.Text,
            SelectionBackColor = Color.FromArgb(24, 56, 90),
            SelectionForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
        };
        _grid.ColumnHeadersDefaultCellStyle = header;

        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            SelectionBackColor = Theme.Accent,
            SelectionForeColor = Color.White,
        };

        AddColumn("Opponent", "Đối thủ", 30, DataGridViewContentAlignment.MiddleLeft);
        AddColumn("Result", "Kết quả", 20, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("TotalShots", "Số phát bắn", 25, DataGridViewContentAlignment.MiddleCenter);
        AddColumn("StartedAt", "Thời gian", 25, DataGridViewContentAlignment.MiddleCenter);
    }

    private void AddColumn(string name, string header, float weight, DataGridViewContentAlignment align)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        column.DefaultCellStyle.Alignment = align;
        _grid.Columns.Add(column);
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        ForeColor = Theme.Accent,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Label StatBox(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BackColor = Theme.Field,
        ForeColor = Theme.Text,
        Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(4),
    };
}
