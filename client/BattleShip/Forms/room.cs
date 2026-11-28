using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Forms;

public sealed class FormRoom : Form
{
    private readonly INetworkClient _client = AppSession.Client;
    private readonly bool _host;
    private RoomInfo _room;
    private readonly CheckBox _lock = new() { Text = "Khóa phòng", AutoSize = true };
    private readonly Label _guest = Theme.FieldLabel("");
    private readonly Label _ready = Theme.FieldLabel("");
    private readonly Label _status = Theme.Status();
    private readonly Button _action = Theme.PrimaryButton("");
    private readonly Button _kick = Theme.PrimaryButton("Mời khách ra");
    private readonly Button _leave = Theme.PrimaryButton("Thoát phòng");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private bool _busy;
    private bool _polling;
    private bool _starting;
    private bool _done;

    public MatchFoundInfo? Match { get; private set; }

    public FormRoom(RoomInfo room, bool host)
    {
        _room = room;
        _host = host;
        Text = "BattleShip - Phòng chờ";
        Theme.ApplyForm(this, 800, 458);
        var title = Theme.Heading("Phòng chờ", 21);
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.SetBounds(24, 12, 550, 52);
        var code = Theme.FieldLabel($"Mã phòng    {room.Code}");
        code.ForeColor = Theme.Text;
        code.SetBounds(24, 85, 210, 34);
        var copy = Theme.PrimaryButton("Sao chép");
        copy.SetBounds(236, 77, 116, 40);
        _lock.SetBounds(620, 86, 140, 34);
        _lock.Visible = host;
        _lock.AutoCheck = false;

        var seats = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 3,
            BackColor = Theme.Field,
            Padding = new Padding(12, 4, 12, 4)
        };
        seats.SetBounds(24, 145, 752, 148);
        foreach (var width in new[] { 45f, 25f, 30f })
            seats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        for (int i = 0; i < 3; i++) seats.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        void Cell(Label label, int col, int row)
        {
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            seats.Controls.Add(label, col, row);
        }
        Cell(Theme.FieldLabel("Người chơi"), 0, 0);
        Cell(Theme.FieldLabel("Vai trò"), 1, 0);
        Cell(Theme.FieldLabel("Trạng thái"), 2, 0);
        Cell(Theme.FieldLabel(room.Host + (host ? " (bạn)" : "")), 0, 1);
        Cell(Theme.FieldLabel("Chủ phòng"), 1, 1);
        Cell(Theme.FieldLabel("Trong phòng"), 2, 1);
        Cell(_guest, 0, 2);
        Cell(Theme.FieldLabel("Khách"), 1, 2);
        Cell(_ready, 2, 2);

        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.SetBounds(24, 311, 752, 52);
        _action.SetBounds(582, 390, 194, 44);
        _leave.SetBounds(24, 390, 146, 44);
        _kick.SetBounds(346, 390, 210, 44);
        _kick.Visible = host;
        Controls.AddRange([title, code, copy, _lock, seats, _status, _leave, _kick, _action]);
        _kick.Click += async (_, _) => await RunAsync(async () =>
        {
            if (_room.Guest is not string guest) return;
            if (MessageBox.Show(this, $"Mời {guest} ra khỏi phòng?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _room = await _client.KickGuestAsync(guest);
        });
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(room.Code); }
            catch (System.Runtime.InteropServices.ExternalException) { Theme.ShowError(_status, "Clipboard đang bận."); }
        };
        _lock.Click += async (_, _) => await RunAsync(async () =>
            _room = await _client.LockRoomAsync(!_room.Locked));
        _action.Click += async (_, _) => await RunAsync(async () =>
        {
            if (_host)
            {
                await _client.StartRoomAsync();
                _starting = true;
            }
            else _room = await _client.SetRoomReadyAsync(!_room.Ready);
        });
        _leave.Click += (_, _) => Close();
        _timer.Tick += async (_, _) => await PollAsync();
        Shown += (_, _) => _timer.Start();
        _client.OnMatchFound += Found;
        Render();
    }

    private void Found(MatchFoundInfo match)
    {
        if (_done) return;
        Match = match;
        Finish(DialogResult.OK);
    }

    private void Finish(DialogResult result)
    {
        _done = true;
        _timer.Stop();
        DialogResult = result;
    }

    private void Render()
    {
        if (_done || IsDisposed) return;
        _guest.Text = _room.Guest == null ? "Chỗ trống" : _room.Guest + (_host ? "" : " (bạn)");
        _ready.Text = _room.Guest == null ? "Chờ người vào" : _room.Ready ? "Đã sẵn sàng" : "Chưa sẵn sàng";
        _ready.ForeColor = _room.Ready ? Theme.Success : Theme.Muted;
        _lock.Checked = _room.Locked;
        _action.Text = _host ? "Bắt đầu" : _room.Ready ? "Hủy sẵn sàng" : "Sẵn sàng";
        _action.Enabled = !_busy && !_starting && (!_host || _room.Ready);
        _kick.Enabled = _host && _room.Guest != null && !_busy && !_starting;
        _lock.Enabled = _leave.Enabled = !_busy && !_starting;
        _status.Text = _starting ? "Đang bắt đầu…" : _room.Guest == null ? "Đang chờ khách vào phòng."
            : _room.Ready ? "Đang chờ chủ phòng bắt đầu." : "Đang chờ khách sẵn sàng.";
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _done || _starting) return;
        _busy = true;
        _generation++;
        Render();
        string? error = null;
        try { await action(); }
        catch (ServerErrorException ex) when (ex.Code is "ROOM_CLOSED" or "ROOM_KICKED") { RoomEnded(ex.Message); }
        catch (Exception ex) { error = ErrorTexts.FromException(ex); }
        finally
        {
            _busy = false;
            Render();
            if (error != null && !_done) Theme.ShowError(_status, error);
        }
    }

    private int _generation;
    private async Task PollAsync()
    {
        if (_busy || _polling || _starting || _done) return;
        int generation = _generation;
        _polling = true;
        try
        {
            var latest = await _client.GetRoomAsync();
            if (_done || generation != _generation || latest == _room) return;
            _room = latest;
            Render();
        }
        catch (ServerErrorException ex) when (ex.Code is "ROOM_CLOSED" or "ROOM_KICKED") { if (!_done) RoomEnded(ex.Message); }
        catch (Exception ex) { if (!_done && !_busy) Theme.ShowError(_status, ErrorTexts.FromException(ex)); }
        finally { _polling = false; }
    }

    private void RoomEnded(string message)
    {
        if (_done) return;
        _timer.Stop();
        _done = true;
        MessageBox.Show(this, message, Text);
        Finish(DialogResult.Cancel);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_done || DialogResult == DialogResult.Abort) return;
        e.Cancel = true;
        if (_busy || _starting) return;
        if (_host && MessageBox.Show(this, "Rời và đóng phòng?", Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        BeginInvoke(new Action(async () => await RunAsync(async () =>
        {
            await _client.LeaveRoomAsync();
            Finish(DialogResult.Cancel);
        })));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _done = true;
            _timer.Dispose();
            _client.OnMatchFound -= Found;
        }
        base.Dispose(disposing);
    }
}
