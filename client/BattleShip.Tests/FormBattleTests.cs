using System.Runtime.ExceptionServices;
using BattleShip.Controls;
using BattleShip.Forms;
using BattleShip.Network;

namespace BattleShip.Tests;

/// <summary>
/// Kiểm thử FormBattle (giao diện). Mỗi bài chạy trên một luồng STA riêng vì WinForms yêu cầu vậy.
/// (Các bài về BoardModel đã có trong BoardModelTests; việc nối mạng đã có trong BattleControllerTests.)
/// </summary>
public class FormBattleTests
{
    private static IReadOnlyList<ShipPlacement> DefaultFleet() =>
    [
        new("CARRIER", 0, 0, Direction.Horizontal),
        new("BATTLESHIP", 0, 2, Direction.Horizontal),
        new("CRUISER", 0, 4, Direction.Horizontal),
        new("SUBMARINE", 0, 6, Direction.Horizontal),
        new("DESTROYER", 0, 8, Direction.Horizontal),
    ];

    /// <summary>Tạo kết quả phát bắn như server gửi (các trường next_turn, game_over không quan trọng với các bài này).</summary>
    private static FireResultInfo Info(int x, int y, ShotResult result, params Cell[] sunkCells) =>
        new(x, y, result, result == ShotResult.Sunk ? "SHIP" : null, sunkCells, Turn.You, false);

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static FormBattle NewForm() => new(DefaultFleet(), "Alice", "Bob") { SilentMode = true };

    [Fact]
    public void Initialization_ShowsFullFleetsAndLocksBoards() => RunSta(() =>
    {
        using var form = NewForm();

        Assert.Equal(5, form.MyRemainingShips);
        Assert.Equal(5, form.EnemyRemainingShips);
        Assert.False(form.IsGameOver);
        Assert.True(form.MyBoard.ShowShips);
        Assert.False(form.EnemyBoard.ShowShips);
        Assert.Equal(BoardMode.Disabled, form.MyBoard.Mode);
        Assert.Equal(BoardMode.Disabled, form.EnemyBoard.Mode); // chưa có lượt thì chưa cho bắn
    });

    [Fact]
    public void MyBoard_ShowsThePlacedShips() => RunSta(() =>
    {
        using var form = NewForm();
        Assert.Equal(5, form.MyBoard.PlacedShips.Count);
        Assert.True(form.MyBoard.AllShipsPlaced);
    });

    [Fact]
    public void TurnSwitching_UnlocksAndLocksTheEnemyBoard() => RunSta(() =>
    {
        using var form = NewForm();

        form.SetTurn(isMyTurn: true);
        Assert.True(form.IsMyTurn);
        Assert.Equal(BoardMode.Target, form.EnemyBoard.Mode);

        form.SetTurn(isMyTurn: false);
        Assert.False(form.IsMyTurn);
        Assert.Equal(BoardMode.Disabled, form.EnemyBoard.Mode);
    });

    [Fact]
    public void PlayerShotResults_UpdateTheEnemyBoardAndFleetCount() => RunSta(() =>
    {
        using var form = NewForm();

        form.ApplyMyShotResult(Info(0, 0, ShotResult.Miss));
        Assert.True(form.EnemyBoard.IsAlreadyShot(0, 0));
        Assert.Equal(5, form.EnemyRemainingShips);

        form.ApplyMyShotResult(Info(1, 1, ShotResult.Hit));
        Assert.True(form.EnemyBoard.IsAlreadyShot(1, 1));
        Assert.Equal(5, form.EnemyRemainingShips); // trúng nhưng chưa chìm

        form.ApplyMyShotResult(Info(2, 2, ShotResult.Sunk, new Cell(2, 2), new Cell(3, 2)));
        Assert.Equal(4, form.EnemyRemainingShips);
    });

    [Fact]
    public void OpponentShotResults_UpdateMyBoardAndFleetCount() => RunSta(() =>
    {
        using var form = NewForm();

        form.ApplyOpponentShot(Info(0, 0, ShotResult.Hit));
        Assert.True(form.MyBoard.IsAlreadyShot(0, 0));
        Assert.Equal(5, form.MyRemainingShips);

        form.ApplyOpponentShot(Info(0, 8, ShotResult.Sunk, new Cell(0, 8), new Cell(1, 8)));
        Assert.Equal(4, form.MyRemainingShips);
    });

    [Fact]
    public void SinkingAllEnemyShips_EndsTheGameAsAWin() => RunSta(() =>
    {
        using var form = NewForm();

        for (var i = 0; i < 5; i++)
            form.ApplyMyShotResult(Info(i, 0, ShotResult.Sunk, new Cell(i, 0)));

        Assert.Equal(0, form.EnemyRemainingShips);
        Assert.True(form.IsGameOver);
    });

    [Fact]
    public void LosingAllShips_EndsTheGameAsALoss() => RunSta(() =>
    {
        using var form = NewForm();

        for (var i = 0; i < 5; i++)
            form.ApplyOpponentShot(Info(i, 0, ShotResult.Sunk, new Cell(i, 0)));

        Assert.Equal(0, form.MyRemainingShips);
        Assert.True(form.IsGameOver);
    });

    [Fact]
    public void EndGame_CanOnlyHappenOnce() => RunSta(() =>
    {
        using var form = NewForm();
        form.EndGame(isWinner: true, "thử");
        Assert.True(form.IsGameOver);

        form.EndGame(isWinner: false, "lần hai"); // không được lỗi dù gọi lại (GAME_OVER của server đến sau)
        Assert.True(form.IsGameOver);
    });
}
