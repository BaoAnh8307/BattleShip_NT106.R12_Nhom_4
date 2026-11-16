using BattleShip.App;

namespace BattleShip.Forms;

/// <summary>
/// Form đăng ký tài khoản. Kiểm tra dữ liệu ở client (tên 3 đến 20 ký tự, mật khẩu 6 đến 64 ký tự, nhập lại khớp)
/// rồi gửi REGISTER. Thành công thì đóng với <see cref="DialogResult.OK"/> và đặt <see cref="RegisteredUsername"/>.
/// </summary>
public class FormRegister : Form
{
    private readonly TextBox _username = Theme.Input();
    private readonly TextBox _password = Theme.Input(password: true);
    private readonly TextBox _confirm = Theme.Input(password: true);
    private readonly Button _registerButton = Theme.PrimaryButton("Đăng ký");
    private readonly LinkLabel _backLink = Theme.Link("Đã có tài khoản? Đăng nhập");
    private readonly Label _status = Theme.Status();
    private bool _busy;

    public FormRegister(string initialUsername = "")
    {
        Text = "BattleShip - Đăng ký";
        Theme.ApplyForm(this, 460, 520);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        _username.Text = initialUsername;

        var column = Theme.Column();
        column.Controls.Add(Theme.Heading("ĐĂNG KÝ TÀI KHOẢN", 20f));
        column.Controls.Add(Theme.FieldLabel("Tên đăng nhập"));
        column.Controls.Add(_username);
        column.Controls.Add(Theme.FieldLabel("Mật khẩu"));
        column.Controls.Add(_password);
        column.Controls.Add(Theme.FieldLabel("Nhập lại mật khẩu"));
        column.Controls.Add(_confirm);
        column.Controls.Add(_registerButton);
        column.Controls.Add(_backLink);
        column.Controls.Add(_status);
        Controls.Add(column);

        AcceptButton = _registerButton;
        _registerButton.Click += async (_, _) => await RegisterAsync();
        _backLink.LinkClicked += (_, _) => GoBack();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                GoBack();
        };
    }

    /// <summary>Tên vừa đăng ký thành công (chỉ có giá trị khi DialogResult là OK).</summary>
    public string RegisteredUsername { get; private set; } = "";

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _username.Focus();
    }

    private async Task RegisterAsync()
    {
        if (_busy)
            return;

        var username = _username.Text.Trim();
        var error = InputRules.ValidateUsername(username)
                    ?? InputRules.ValidatePassword(_password.Text)
                    ?? InputRules.ValidateConfirm(_password.Text, _confirm.Text);
        if (error is not null)
        {
            Theme.ShowError(_status, error);
            return;
        }

        SetBusy(true);
        Theme.ShowProgress(_status, "Đang kết nối tới server...");
        try
        {
            await AppSession.EnsureConnectedAsync();
            Theme.ShowProgress(_status, "Đang đăng ký...");
            await AppSession.Client.RegisterAsync(username, _password.Text);
            RegisteredUsername = username;
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

    private void GoBack()
    {
        if (!_busy)
            DialogResult = DialogResult.Cancel;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _registerButton.Enabled = !busy;
        _username.Enabled = !busy;
        _password.Enabled = !busy;
        _confirm.Enabled = !busy;
    }
}
