using BattleShip.Network;

namespace BattleShip.Forms;

/// <summary>Cầu nối giữa <see cref="FormBattle"/> và <see cref="IBattleView"/>. Không sửa FormBattle.</summary>
public sealed class FormBattleView : IBattleView
{
    private readonly FormBattle _form;

    public FormBattleView(FormBattle form)
    {
        _form = form;
        _form.FireRequested += (x, y) => FireRequested?.Invoke(x, y);
        _form.SurrenderRequested += () => SurrenderRequested?.Invoke();
        _form.LeaveRequested += () => LeaveRequested?.Invoke();
        _form.FormClosing += OnFormClosing;
    }

    public bool IsGameOver => _form.IsGameOver;

    public event Action<int, int>? FireRequested;
    public event Action? SurrenderRequested;
    public event Action? LeaveRequested;

    public void SetTurn(bool isMyTurn) => _form.SetTurn(isMyTurn);

    public void ApplyMyShotResult(FireResultInfo result) => _form.ApplyMyShotResult(result);

    public void ApplyOpponentShot(FireResultInfo result) => _form.ApplyOpponentShot(result);

    public void EndGame(bool isWinner, string reason) => _form.EndGame(isWinner, reason);

    public void ShowError(string message)
    {
        if (_form.IsDisposed)
            return;
        if (_form.InvokeRequired)
        {
            _form.BeginInvoke(() => ShowError(message));
            return;
        }

        _form.AddLog(message, Theme.Error);
        Theme.ShowError(_form.StatusLabel, message);
    }

    public void Close(BattleExit exit)
    {
        if (_form.IsDisposed)
            return;
        if (_form.InvokeRequired)
        {
            _form.BeginInvoke(() => Close(exit));
            return;
        }

        _form.DialogResult = exit == BattleExit.BackToLobby ? DialogResult.OK : DialogResult.Abort;
    }

    /// <summary>Bấm dấu X khi ván chưa kết thúc: hỏi rồi coi như đầu hàng (nếu không, đối thủ bị kẹt).</summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_form.IsGameOver || _form.DialogResult != DialogResult.None)
            return;

        var answer = MessageBox.Show(_form, "Thoát giữa ván sẽ tính là bạn đầu hàng. Tiếp tục?",
            "BattleShip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        SurrenderRequested?.Invoke();
    }
}
