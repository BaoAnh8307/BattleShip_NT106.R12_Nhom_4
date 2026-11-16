using BattleShip.App;

namespace BattleShip.Forms;

/// <summary>
/// Form đăng nhập. Địa chỉ server và cổng lấy từ appsettings.json (không nhập ở đây).
/// Đăng nhập thành công thì đóng với <see cref="DialogResult.OK"/>; người chơi hiện tại nằm trong
/// <see cref="AppSession.CurrentUser"/>. Đóng cửa sổ thì trả về Cancel (thoát ứng dụng).
/// </summary>
public class FormLogin : Form
{
    private readonly TextBox _username = Theme.Input();
    private readonly TextBox _password = Theme.Input(password: true);
    private readonly Button _loginButton = Theme.PrimaryButton("Đăng nhập");
    private readonly LinkLabel _registerLink = Theme.Link("Chưa có tài khoản? Đăng ký");
    private readonly Label _status = Theme.Status();
    private bool _busy;

    public FormLogin()
    {
        Text = "BattleShip - Đăng nhập";
        Theme.ApplyForm(this, 460, 440);

        var column = Theme.Column();
        column.Controls.Add(Theme.Heading("BATTLESHIP"));
        column.Controls.Add(Theme.FieldLabel("Tên đăng nhập"));
        column.Controls.Add(_username);
        column.Controls.Add(Theme.FieldLabel("Mật khẩu"));
        column.Controls.Add(_password);
        column.Controls.Add(_loginButton);
        column.Controls.Add(_registerLink);
        column.Controls.Add(_status);
        Controls.Add(column);

        AcceptButton = _loginButton; // Enter = Đăng nhập
        _loginButton.Click += async (_, _) => await LoginAsync();
        _registerLink.LinkClicked += (_, _) => OpenRegister();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _username.Focus();
    }

    private async Task LoginAsync()
    {
        if (_busy)
            return;

        var username = _username.Text.Trim();
        var password = _password.Text;
        if (username.Length == 0 || password.Length == 0)
        {
            Theme.ShowError(_status, "Hãy nhập tên đăng nhập và mật khẩu.");
            return;
        }

        SetBusy(true);
        Theme.ShowProgress(_status, "Đang kết nối tới server...");
        try
        {
            await AppSession.EnsureConnectedAsync();
            Theme.ShowProgress(_status, "Đang đăng nhập...");
            AppSession.CurrentUser = await AppSession.Client.LoginAsync(username, password);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            Theme.ShowError(_status, ErrorTexts.FromException(ex, AppSession.Config));
        }
        finally
        {
            if (!IsDisposed)
                SetBusy(false);
        }
    }

    private void OpenRegister()
    {
        if (_busy)
            return;

        using var register = new FormRegister(_username.Text.Trim());
        if (register.ShowDialog(this) == DialogResult.OK)
        {
            _username.Text = register.RegisteredUsername;
            _password.Clear();
            _password.Focus();
            Theme.ShowInfo(_status, "Đăng ký thành công. Hãy đăng nhập.");
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _loginButton.Enabled = !busy;
        _username.Enabled = !busy;
        _password.Enabled = !busy;
    }
}
