namespace BattleShip.Forms;

/// <summary>Màu sắc, phông chữ và hàm tạo control dùng chung để các form giống nhau (nền tối, nút xanh).</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(14, 34, 56);
    public static readonly Color Field = Color.FromArgb(11, 27, 45);
    public static readonly Color Text = Color.FromArgb(230, 238, 247);
    public static readonly Color Muted = Color.FromArgb(143, 166, 191);
    public static readonly Color Accent = Color.FromArgb(46, 134, 222);
    public static readonly Color AccentHover = Color.FromArgb(70, 150, 235);
    public static readonly Color Error = Color.FromArgb(240, 110, 115);
    public static readonly Color Success = Color.FromArgb(47, 180, 124);

    /// <summary>Độ rộng các ô nhập và nút (px, ở 100% DPI).</summary>
    public const int ContentWidth = 380;

    public static void ApplyForm(Form form, int width, int height)
    {
        form.AutoScaleDimensions = new SizeF(96f, 96f);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = new Size(width, height);
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = new Font("Segoe UI", 10f);
    }

    /// <summary>Cột dọc chứa các control, xếp từ trên xuống.</summary>
    public static FlowLayoutPanel Column() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(40, 28, 40, 16),
        BackColor = Background,
    };

    public static Label Heading(string text, float size = 24f) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", size, FontStyle.Bold),
        ForeColor = Accent,
        AutoSize = false,
        Width = ContentWidth,
        Height = 56,
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(0, 0, 0, 14),
    };

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = false,
        Width = ContentWidth,
        Height = 22,
        Margin = new Padding(0, 0, 0, 2),
    };

    public static TextBox Input(bool password = false) => new()
    {
        Width = ContentWidth,
        BackColor = Field,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 11f),
        UseSystemPasswordChar = password,
        Margin = new Padding(0, 0, 0, 14),
    };

    public static Button PrimaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Width = ContentWidth,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 6, 0, 10),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        return button;
    }

    public static LinkLabel Link(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = ContentWidth,
        Height = 26,
        TextAlign = ContentAlignment.MiddleCenter,
        LinkColor = Accent,
        ActiveLinkColor = AccentHover,
        Margin = new Padding(0, 0, 0, 6),
    };

    public static Label Status() => new()
    {
        AutoSize = false,
        Width = ContentWidth,
        Height = 44,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Muted,
        Margin = new Padding(0),
    };

    public static void ShowError(Label status, string message)
    {
        status.ForeColor = Error;
        status.Text = message;
    }

    public static void ShowInfo(Label status, string message)
    {
        status.ForeColor = Success;
        status.Text = message;
    }

    public static void ShowProgress(Label status, string message)
    {
        status.ForeColor = Muted;
        status.Text = message;
    }
}
