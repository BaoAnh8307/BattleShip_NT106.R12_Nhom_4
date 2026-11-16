using BattleShip.App;
using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>
/// Màn tạm để thử luồng đăng nhập và FormPlaceShips, sẽ được thay bằng FormLobby thật.
/// Đăng xuất trả về <see cref="DialogResult.Retry"/> (quay lại đăng nhập); Thoát trả về Cancel.
/// Nút "Tìm trận" chạy đúng luồng của sảnh thật: FIND_MATCH, chờ MATCH_FOUND, mở FormPlaceShips.
/// </summary>
public class FormLobbyTemp : Form
{
    public FormLobbyTemp()
    {
        Text = "BattleShip - Sảnh (tạm)";
        Theme.ApplyForm(this, 460, 400);

        var greeting = Theme.Heading($"Xin chào, {AppSession.CurrentUser?.Username}", 18f);
        greeting.ForeColor = Theme.Text;

        var note = Theme.Status();
        note.Text = "Sảnh thật do bạn khác làm. Màn này chỉ để thử.";

        var find = Theme.PrimaryButton("Tìm trận (thử đặt tàu)");
        find.Click += async (_, _) => await FindAndPlaceAsync(find);

        var logout = Theme.PrimaryButton("Đăng xuất");
        logout.BackColor = Color.FromArgb(38, 56, 75);
        logout.Click += async (_, _) =>
        {
            await AppSession.LogoutAsync();
            DialogResult = DialogResult.Retry;
        };

        var exit = Theme.PrimaryButton("Thoát");
        exit.BackColor = Color.FromArgb(38, 56, 75);
        exit.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var column = Theme.Column();
        column.Controls.Add(greeting);
        column.Controls.Add(note);
        column.Controls.Add(find);
        column.Controls.Add(logout);
        column.Controls.Add(exit);
        Controls.Add(column);
    }

    private async Task FindAndPlaceAsync(Button button)
    {
        button.Enabled = false;
        var found = new TaskCompletionSource<MatchFoundInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFound(MatchFoundInfo info) => found.TrySetResult(info);

        AppSession.Client.OnMatchFound += OnFound;
        try
        {
            await AppSession.Client.FindMatchAsync();
            var match = await found.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var place = new FormPlaceShips(match);
            var result = place.ShowDialog(this);
            MessageBox.Show(this,
                $"FormPlaceShips đóng với kết quả: {result}\nSố tàu đã gửi: {place.PlacedShips.Count}",
                "BattleShip", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Ván giả vẫn đang chạy: kết thúc để lần sau tìm trận lại được.
            if (result == DialogResult.OK)
                await AppSession.Client.SurrenderAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ErrorTexts.FromException(ex, AppSession.Config),
                "BattleShip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            AppSession.Client.OnMatchFound -= OnFound;
            if (!IsDisposed)
                button.Enabled = true;
        }
    }
}
