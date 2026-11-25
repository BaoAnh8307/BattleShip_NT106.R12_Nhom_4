using BattleShip.App;
using BattleShip.Forms;

namespace BattleShip;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Cài sẵn context của WinForms để event của client luôn quay về luồng giao diện.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        try
        {
            AppSession.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "BattleShip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }   

        // Vòng đời: Đăng nhập -> Sảnh -> (Đăng xuất) -> Đăng nhập ...
        while (true)
        {
            using (var login = new FormLogin())
            {
                if (login.ShowDialog() != DialogResult.OK)
                    break;
            }

            using var lobby = new FormLobby();
            if (lobby.ShowDialog() != DialogResult.Retry)
                break;
        }

        Task.Run(AppSession.ShutdownAsync).GetAwaiter().GetResult();
        BattleShip.Controls.ShipSpriteCache.Shared.Dispose();
    }
}
