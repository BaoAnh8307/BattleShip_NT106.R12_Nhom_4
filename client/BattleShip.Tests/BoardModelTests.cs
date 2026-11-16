using BattleShip.Controls;
using BattleShip.Network;

namespace BattleShip.Tests;

/// <summary>Kiểm thử logic bàn cờ (đặt tàu, đánh dấu phát bắn, tính ô từ vị trí chuột). Không cần giao diện.</summary>
public class BoardModelTests
{
    private static ShipPlacement Ship(string name, int x, int y, Direction dir = Direction.Horizontal) =>
        new(name, x, y, dir);

    private static ShipSpec Spec(string name) => Fleet.Find(name)!;

    // ----- Đặt tàu -----

    [Fact]
    public void CanPlace_ValidShip_ReturnsTrue()
    {
        var board = new BoardModel();
        Assert.True(board.CanPlace(Ship("CARRIER", 0, 0), out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void CanPlace_OutOfBounds_ReturnsFalse()
    {
        var board = new BoardModel();
        Assert.False(board.CanPlace(Ship("CARRIER", 7, 0), out var reason)); // x = 7..11
        Assert.NotNull(reason);
        Assert.False(board.CanPlace(Ship("CARRIER", 0, 7, Direction.Vertical), out _)); // y = 7..11
        Assert.False(board.CanPlace(Ship("DESTROYER", -1, 0), out _));
    }

    [Fact]
    public void CanPlace_OverlappingShip_ReturnsFalse()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CARRIER", 0, 0), out _);
        Assert.False(board.CanPlace(Ship("BATTLESHIP", 2, 0, Direction.Vertical), out var reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void CanPlace_AdjacentShips_IsAllowed()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CARRIER", 0, 0), out _);
        Assert.True(board.CanPlace(Ship("BATTLESHIP", 0, 1), out _)); // sát ngay bên dưới
        Assert.True(board.CanPlace(Ship("CRUISER", 5, 0), out _));    // sát ngay bên phải
    }

    [Fact]
    public void CanPlace_UnknownName_ReturnsFalse()
    {
        var board = new BoardModel();
        Assert.False(board.CanPlace(Ship("GUNBOAT", 0, 0), out var reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryPlace_SameNameAgain_ReplacesPreviousShip()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CARRIER", 0, 0), out _);
        Assert.True(board.TryPlace(Ship("CARRIER", 0, 5), out _)); // đặt lại ở chỗ khác, kể cả chồng chỗ cũ
        Assert.Equal(1, board.Ships.Count);
        Assert.Equal(5, board.Ships[0].Y);
    }

    [Fact]
    public void TryPlace_Rejected_DoesNotChangeBoard()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CARRIER", 0, 0), out _);
        Assert.False(board.TryPlace(Ship("BATTLESHIP", 0, 0), out _));
        Assert.Equal(1, board.Ships.Count);
    }

    [Fact]
    public void AllShipsPlaced_OnlyAfterAllFiveShips()
    {
        var board = new BoardModel();
        for (var i = 0; i < Fleet.Ships.Count; i++)
        {
            Assert.False(board.AllShipsPlaced);
            Assert.True(board.TryPlace(Ship(Fleet.Ships[i].Name, 0, i), out _));
        }
        Assert.True(board.AllShipsPlaced);
    }

    [Fact]
    public void RemoveShip_RemovesIt_AndClearShipsRemovesAll()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CARRIER", 0, 0), out _);
        board.TryPlace(Ship("DESTROYER", 0, 2), out _);

        Assert.True(board.RemoveShip("CARRIER"));
        Assert.False(board.RemoveShip("CARRIER"));
        Assert.Equal(1, board.Ships.Count);

        board.ClearShips();
        Assert.Equal(0, board.Ships.Count);
    }

    [Fact]
    public void ShipNameAt_ReturnsNameOfShipOnThatCell()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CRUISER", 4, 4, Direction.Vertical), out _);
        Assert.Equal("CRUISER", board.ShipNameAt(new Cell(4, 6)));
        Assert.Null(board.ShipNameAt(new Cell(5, 4)));
    }

    [Fact]
    public void LoadShips_ReplacesExistingShips()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CARRIER", 0, 0), out _);
        board.LoadShips([Ship("DESTROYER", 3, 3), Ship("SUBMARINE", 0, 9)]);
        Assert.Equal(2, board.Ships.Count);
        Assert.Null(board.ShipNameAt(new Cell(0, 0)));
    }

    [Fact]
    public void PreviewCells_AndValidity_FollowDirectionAndBounds()
    {
        var board = new BoardModel();
        var cells = board.PreviewCells(new Cell(2, 3), Spec("CRUISER"), Direction.Vertical);
        Assert.Equal(3, cells.Count);
        Assert.Equal(new Cell(2, 5), cells[2]);

        Assert.True(board.IsValidPreview(new Cell(2, 3), Spec("CRUISER"), Direction.Vertical));
        Assert.False(board.IsValidPreview(new Cell(2, 8), Spec("CRUISER"), Direction.Vertical)); // ra ngoài
    }

    // ----- Phát bắn -----

    [Fact]
    public void ApplyShot_MissAndHit_MarkTheCell()
    {
        var board = new BoardModel();
        board.ApplyShot(1, 1, ShotResult.Miss);
        board.ApplyShot(2, 2, ShotResult.Hit);
        Assert.Equal(CellMark.Miss, board.MarkAt(1, 1));
        Assert.Equal(CellMark.Hit, board.MarkAt(2, 2));
        Assert.Equal(CellMark.Empty, board.MarkAt(3, 3));
    }

    [Fact]
    public void ApplyShot_Sunk_MarksAllCellsOfTheShip()
    {
        var board = new BoardModel();
        board.ApplyShot(3, 7, ShotResult.Hit);
        board.ApplyShot(3, 8, ShotResult.Sunk, [new Cell(3, 7), new Cell(3, 8)]);
        Assert.Equal(CellMark.Sunk, board.MarkAt(3, 7));
        Assert.Equal(CellMark.Sunk, board.MarkAt(3, 8));
    }

    [Fact]
    public void ApplyShot_OutOfBounds_Throws()
    {
        var board = new BoardModel();
        Assert.Throws<ArgumentOutOfRangeException>(() => board.ApplyShot(10, 0, ShotResult.Miss));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.MarkAt(0, -1));
    }

    [Fact]
    public void AlreadyShot_IsFalseUntilShot()
    {
        var board = new BoardModel();
        Assert.False(board.AlreadyShot(4, 4));
        board.ApplyShot(4, 4, ShotResult.Miss);
        Assert.True(board.AlreadyShot(4, 4));
        Assert.False(board.AlreadyShot(99, 99)); // ngoài bảng thì không coi là đã bắn
    }

    [Fact]
    public void Reset_ClearsShipsAndMarks()
    {
        var board = new BoardModel();
        board.TryPlace(Ship("CARRIER", 0, 0), out _);
        board.ApplyShot(5, 5, ShotResult.Hit);
        board.Reset();
        Assert.Equal(0, board.Ships.Count);
        Assert.Equal(CellMark.Empty, board.MarkAt(5, 5));
    }

    // ----- Bố cục và vị trí chuột -----

    [Fact]
    public void Compute_CentersBoardInSquareControl()
    {
        var layout = BoardGeometry.Compute(340, 340);
        Assert.Equal(33, layout.CellSize);
        Assert.Equal(330, layout.TotalSize);
        Assert.Equal(5, layout.OriginX);
        Assert.Equal(5, layout.OriginY);
    }

    [Fact]
    public void Compute_WithLabelSpace_ShiftsOrigin()
    {
        var layout = BoardGeometry.Compute(358, 358, 18);
        Assert.Equal(33, layout.CellSize);
        Assert.Equal(23, layout.OriginX);
        Assert.Equal(23, layout.OriginY);
    }

    [Fact]
    public void Compute_TinyControl_StillHasPositiveCellSize()
    {
        Assert.True(BoardGeometry.Compute(5, 5).CellSize >= 1);
    }

    [Fact]
    public void TryHitTest_MapsPixelsToCells()
    {
        var layout = BoardGeometry.Compute(340, 340); // gốc (5,5), ô 33px
        Assert.True(BoardGeometry.TryHitTest(layout, 5, 5, out var topLeft));
        Assert.Equal(new Cell(0, 0), topLeft);

        Assert.True(BoardGeometry.TryHitTest(layout, 334, 334, out var bottomRight));
        Assert.Equal(new Cell(9, 9), bottomRight);

        Assert.True(BoardGeometry.TryHitTest(layout, 5 + 33 * 3 + 10, 5 + 33 * 7 + 1, out var middle));
        Assert.Equal(new Cell(3, 7), middle);
    }

    [Fact]
    public void TryHitTest_OutsideBoard_ReturnsFalse()
    {
        var layout = BoardGeometry.Compute(340, 340);
        Assert.False(BoardGeometry.TryHitTest(layout, 4, 100, out _));   // sát lề trái
        Assert.False(BoardGeometry.TryHitTest(layout, 100, 4, out _));   // sát lề trên
        Assert.False(BoardGeometry.TryHitTest(layout, 335, 100, out _)); // ngay sau ô cuối
        Assert.False(BoardGeometry.TryHitTest(layout, 100, 335, out _));
    }
}
