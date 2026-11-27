using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Forms;

public sealed class FormLobby : Form
{
    private readonly INetworkClient _client = AppSession.Client;
    private readonly Button _create = Theme.PrimaryButton("Tạo phòng");
    private readonly Button _find = Theme.PrimaryButton("Tìm trận");
    private readonly Button _logout = Theme.PrimaryButton("Đăng xuất");
    private readonly Button _stats = Theme.PrimaryButton("Thống kê / Lịch sử");
    private readonly Label _status = Theme.Status();
    private readonly Func<Form>? _createStats;
    private bool _waiting;
    private bool _placing;
    private bool _leaving;

    public FormLobby(Func<Form>? createStats = null)
    {
        _createStats = createStats;
        Text = "BattleShip - Sảnh";
        Theme.ApplyForm(this, 800, 458);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Field,
            Padding = new Padding(12, 4, 4, 4),
            ColumnCount = 2,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));

        var greeting = Theme.FieldLabel($"Xin chào, {AppSession.CurrentUser?.Username}");
        greeting.ForeColor = Theme.Text;
        greeting.Dock = DockStyle.Fill;
        greeting.TextAlign = ContentAlignment.MiddleLeft;
        _logout.Dock = DockStyle.Fill;
        _logout.Margin = new Padding(2);

        header.Controls.Add(greeting, 0, 0);
        header.Controls.Add(_logout, 1, 0);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Bottom };
        var heading = Theme.Heading("Chơi cùng bạn bè", 20);
        heading.Anchor = AnchorStyles.Bottom;
        void AddAction(Button button, string caption)
        {
            var choice = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Size = new Size(240, 96),
                Margin = new Padding(8, 0, 8, 0)
            };
            button.Size = new Size(240, 60);
            var hint = Theme.FieldLabel(caption);
            hint.Width = 240;
            hint.TextAlign = ContentAlignment.MiddleCenter;
            choice.Controls.AddRange([button, hint]);
            actions.Controls.Add(choice);
        }
        AddAction(_create, "Làm chủ phòng mới");
        AddAction(_find, "Xem các phòng còn chỗ");
        _status.Dock = DockStyle.Fill;
        _stats.Width = 200;
        _stats.Anchor = AnchorStyles.None;

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(heading, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(_stats, 0, 4);
        Controls.Add(layout);

        _find.Click += async (_, _) => await OpenRoomsAsync(false);
        _create.Click += async (_, _) => await OpenRoomsAsync(true);
        _logout.Click += async (_, _) => await ReturnToLoginAsync();
        _stats.Click += (_, _) =>
        {
            if (_createStats is null) return;
            using var stats = _createStats();
            stats.ShowDialog(this);
        };
        UpdateButtons();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _client.OnDisconnected += Disconnected;
        _client.OnGameOver += GameOver;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _leaving = true;
        _client.OnDisconnected -= Disconnected;
        _client.OnGameOver -= GameOver;
        base.OnFormClosed(e);
    }

    private void UpdateButtons()
    {
        bool idle = !_leaving && !_waiting && !_placing && _client.State == SessionState.Authed;
        _create.Enabled = _find.Enabled = idle;
        _stats.Enabled = idle && _createStats != null;
        _logout.Enabled = !_leaving && !_waiting;
    }

    private async Task OpenRoomsAsync(bool host)
    {
        if (_waiting || _leaving) return;
        _waiting = true;
        UpdateButtons();
        try
        {
            do
            {
                RoomInfo room;
                if (host) room = await _client.CreateRoomAsync();
                else
                {
                    using var list = new FormRoomList();
                    if (list.ShowDialog(this) != DialogResult.OK || list.JoinedRoom == null) return;
                    room = list.JoinedRoom;
                }
                if (_leaving) return;
                using var waitingRoom = new FormRoom(room, host);
                waitingRoom.ShowDialog(this);
                if (_leaving || _client.State == SessionState.Disconnected) return;
                if (waitingRoom.Match is { } match)
                {
                    await PlayMatchAsync(match);
                    return;
                }
            } while (!host);
        }
        catch (Exception ex)
        {
            if (!_leaving) Theme.ShowError(_status, ErrorTexts.FromException(ex, AppSession.Config));
        }
        finally
        {
            _waiting = false;
            if (!_leaving) UpdateButtons();
        }
    }

    private async Task PlayMatchAsync(MatchFoundInfo match)
    {
        _placing = true;
        UpdateButtons();
        Theme.ShowInfo(_status, $"Đã tìm thấy đối thủ: {match.Opponent}");

        using var place = new FormPlaceShips(match);
        var result = place.ShowDialog(this);

        if (result == DialogResult.Abort || _client.State == SessionState.Disconnected)
        {
            _placing = false;
            // FormPlaceShips đã báo lý do mất kết nối.
            await ReturnToLoginAsync();
            return;
        }

        if (result == DialogResult.OK)
        {
            var battleResult = await BattleFlow.RunAsync(this, match, place.PlacedShips);
            _placing = false;

            if (battleResult == DialogResult.Abort || _client.State == SessionState.Disconnected)
            {
                // FormBattleView đã báo lý do mất kết nối.
                await ReturnToLoginAsync();
                return;
            }

            Theme.ShowInfo(_status, "Ván đã kết thúc. Bạn có thể tìm trận mới.");
        }
        else
        {
            _placing = false;
            Theme.ShowProgress(_status, "Đã quay lại sảnh.");
        }
        UpdateButtons();
    }

    private void GameOver(GameOverInfo result)
    {
        if (_leaving || _placing) return;
        Theme.ShowInfo(_status, "Ván đã kết thúc. Bạn có thể tìm trận mới.");
        UpdateButtons();
    }

    private async void Disconnected(string reason)
    {
        if (_leaving || _placing) return;
        await ReturnToLoginAsync(reason);
    }

    private async Task ReturnToLoginAsync(string? reason = null)
    {
        if (_leaving) return;
        _leaving = true;
        UpdateButtons();
        foreach (var child in OwnedForms) child.DialogResult = DialogResult.Abort;
        if (reason != null)
            MessageBox.Show(this, reason, "Mất kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        try
        {
            await AppSession.LogoutAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ErrorTexts.FromException(ex, AppSession.Config),
                "BattleShip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        if (!IsDisposed)
            DialogResult = DialogResult.Retry;
    }
}
