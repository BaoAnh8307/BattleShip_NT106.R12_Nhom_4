using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Forms;

public sealed class FormRoomList : Form
{
    private readonly INetworkClient _client = AppSession.Client;
    private readonly TextBox _search = Theme.Input();
    private readonly ListView _list = new();
    private readonly Label _status = Theme.Status();
    private readonly Button _join = Theme.PrimaryButton("Vào phòng");
    private readonly Button _refresh = Theme.PrimaryButton("Làm mới");
    private RoomInfo[] _rooms = [];
    private bool _busy;

    public RoomInfo? JoinedRoom { get; private set; }

    public FormRoomList()
    {
        Text = "BattleShip - Danh sách phòng";
        Theme.ApplyForm(this, 800, 458);
        var title = Theme.Heading("Danh sách phòng", 21);
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.SetBounds(24, 12, 420, 52);
        _refresh.SetBounds(626, 20, 150, 40);
        _search.PlaceholderText = "Tìm theo mã phòng hoặc tên chủ phòng";
        _search.SetBounds(24, 83, 752, 32);

        _list.SetBounds(24, 132, 752, 196);
        _list.View = View.Details;
        _list.MultiSelect = false;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.BackColor = Theme.Field;
        _list.ForeColor = Theme.Text;
        _list.Columns.Add("Mã phòng", 180);
        _list.Columns.Add("Chủ phòng", 252);
        _list.Columns.Add("Số người", 132);
        _list.Columns.Add("Trạng thái", 150);

        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.SetBounds(24, 338, 752, 44);
        var back = Theme.PrimaryButton("Về sảnh");
        back.SetBounds(24, 390, 142, 44);
        _join.SetBounds(610, 390, 166, 44);
        Controls.AddRange([title, _refresh, _search, _list, _status, back, _join]);

        _search.TextChanged += (_, _) => FilterRooms();
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += async (_, _) => await JoinAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _join.Click += async (_, _) => await JoinAsync();
        back.Click += (_, _) => Close();
        Shown += async (_, _) => await RefreshAsync();
    }

    private void FilterRooms()
    {
        string query = _search.Text.Trim();
        string? selected = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Text : null;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var room in _rooms)
        {
            if (!room.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !room.Host.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var item = new ListViewItem(new[] { room.Code, room.Host, "1/2", "Còn chỗ" });
            _list.Items.Add(item);
            item.Selected = room.Code == selected;
        }
        _list.EndUpdate();
        Theme.ShowProgress(_status, _list.Items.Count == 0
            ? "Không có phòng phù hợp." : $"{_list.Items.Count} phòng có thể vào");
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _refresh.Enabled = !_busy;
        _join.Enabled = !_busy && _list.SelectedItems.Count == 1;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        try
        {
            _rooms = await _client.ListRoomsAsync();
            if (Visible) FilterRooms();
        }
        catch (Exception ex) { Theme.ShowError(_status, ErrorTexts.FromException(ex)); }
        finally { _busy = false; UpdateButtons(); }
    }

    private async Task JoinAsync()
    {
        if (!_join.Enabled) return;
        string code = _list.SelectedItems[0].Text;
        _busy = true;
        UpdateButtons();
        try
        {
            JoinedRoom = await _client.JoinRoomAsync(code);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _busy = false;
            await RefreshAsync();
            Theme.ShowError(_status, ErrorTexts.FromException(ex));
        }
        finally { _busy = false; UpdateButtons(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy && DialogResult != DialogResult.Abort && JoinedRoom == null) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
