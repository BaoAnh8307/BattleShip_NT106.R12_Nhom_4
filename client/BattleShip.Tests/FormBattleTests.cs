using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using BattleShip.Controls;
using BattleShip.Forms;
using BattleShip.Network;

namespace BattleShip.Tests;

/// <summary>Chạy một hành động trên luồng STA riêng (WinForms yêu cầu), rồi ném lại lỗi nếu có.</summary>
internal static class Sta
{
    public static void Run(Action action)
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
}

// =====================================================================================
// 1. Ảnh tàu: đặt tên file, nạp, fallback, cache, Dispose
// =====================================================================================

public class ShipSpriteTests
{
    private const int CellPixels = 96;

    private static void WithTempFolder(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "battleship_sprites_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            test(folder);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static void WritePng(string folder, string fileName, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Crimson);
        bitmap.Save(Path.Combine(folder, fileName), ImageFormat.Png);
    }

    // ----- Đặt tên file (thuần chuỗi) -----

    [Fact]
    public void NamedFile_UsesLowercaseShipNameAndDirectionLetter()
    {
        Assert.Equal("carrier_H.png", ShipSpriteNaming.NamedFile("CARRIER", Direction.Horizontal));
        Assert.Equal("battleship_V.png", ShipSpriteNaming.NamedFile("BATTLESHIP", Direction.Vertical));
        Assert.Equal("destroyer_H.png", ShipSpriteNaming.NamedFile("Destroyer", Direction.Horizontal));
    }

    [Fact]
    public void LengthFile_UsesLengthAndDirectionLetter()
    {
        Assert.Equal("len1_H.png", ShipSpriteNaming.LengthFile(1, Direction.Horizontal));
        Assert.Equal("len3_V.png", ShipSpriteNaming.LengthFile(3, Direction.Vertical));
        Assert.Equal("len5_H.png", ShipSpriteNaming.LengthFile(5, Direction.Horizontal));
    }

    [Fact]
    public void Candidates_TryNamedFileBeforeLengthFile()
    {
        var candidates = ShipSpriteNaming.Candidates("CRUISER", 3, Direction.Vertical);
        Assert.Equal(2, candidates.Count);
        Assert.Equal("cruiser_V.png", candidates[0]);
        Assert.Equal("len3_V.png", candidates[1]);
    }

    [Fact]
    public void DefaultDirectory_IsAssetsShipsNextToTheExecutable()
    {
        var expected = Path.Combine(AppContext.BaseDirectory, "Assets", "Ships");
        Assert.Equal(expected, ShipSpriteNaming.DefaultDirectory);
    }

    // ----- Nạp ảnh và fallback -----

    [Fact]
    public void Get_PrefersTheNamedFile_OverTheLengthFile() => WithTempFolder(folder =>
    {
        WritePng(folder, "carrier_H.png", 50, 10);
        WritePng(folder, "len5_H.png", 60, 10);

        using var cache = new ShipSpriteCache(folder);
        var image = cache.Get("CARRIER", 5, Direction.Horizontal);

        Assert.NotNull(image);
        Assert.Equal(50, image!.Width); // ảnh theo tên, không phải ảnh theo độ dài
    });

    [Fact]
    public void Get_FallsBackToTheLengthFile_WhenTheNamedFileIsMissing() => WithTempFolder(folder =>
    {
        WritePng(folder, "len3_V.png", 10, 30);

        using var cache = new ShipSpriteCache(folder);
        var image = cache.Get("SUBMARINE", 3, Direction.Vertical);

        Assert.NotNull(image);
        Assert.Equal(30, image!.Height);
    });

    [Fact]
    public void Get_ReturnsNull_WhenNoFileExists_OrTheFolderIsMissing() => WithTempFolder(folder =>
    {
        using var emptyFolder = new ShipSpriteCache(folder);
        Assert.Null(emptyFolder.Get("CARRIER", 5, Direction.Horizontal));

        using var missingFolder = new ShipSpriteCache(Path.Combine(folder, "khong_ton_tai"));
        Assert.Null(missingFolder.Get("CARRIER", 5, Direction.Horizontal));
    });

    [Fact]
    public void Get_ReturnsNull_ForACorruptFile_WithoutThrowing() => WithTempFolder(folder =>
    {
        File.WriteAllBytes(Path.Combine(folder, "carrier_H.png"), [1, 2, 3, 4, 5]);

        using var cache = new ShipSpriteCache(folder);
        Assert.Null(cache.Get("CARRIER", 5, Direction.Horizontal));
    });

    [Fact]
    public void Get_CorruptNamedFile_FallsBackToTheLengthFile() => WithTempFolder(folder =>
    {
        File.WriteAllBytes(Path.Combine(folder, "carrier_H.png"), [9, 9, 9]);
        WritePng(folder, "len5_H.png", 70, 10);

        using var cache = new ShipSpriteCache(folder);
        var image = cache.Get("CARRIER", 5, Direction.Horizontal);

        Assert.NotNull(image);
        Assert.Equal(70, image!.Width);
    });

    // ----- Cache và Dispose -----

    [Fact]
    public void Get_CachesTheLoadedImage() => WithTempFolder(folder =>
    {
        WritePng(folder, "cruiser_H.png", 30, 10);

        using var cache = new ShipSpriteCache(folder);
        var first = cache.Get("CRUISER", 3, Direction.Horizontal);
        var second = cache.Get("CRUISER", 3, Direction.Horizontal);

        Assert.True(ReferenceEquals(first, second), "Lần gọi thứ hai phải trả về cùng một ảnh trong cache");
        Assert.Equal(1, cache.LoadedCount);
    });

    [Fact]
    public void Get_DoesNotLockTheFileOnDisk() => WithTempFolder(folder =>
    {
        WritePng(folder, "cruiser_H.png", 30, 10);

        using var cache = new ShipSpriteCache(folder);
        Assert.NotNull(cache.Get("CRUISER", 3, Direction.Horizontal));

        File.Delete(Path.Combine(folder, "cruiser_H.png")); // sẽ ném lỗi nếu file đang bị khóa
        Assert.False(File.Exists(Path.Combine(folder, "cruiser_H.png")));
    });

    [Fact]
    public void Dispose_ClearsTheCache_AndLaterGetReturnsNull() => WithTempFolder(folder =>
    {
        WritePng(folder, "cruiser_H.png", 30, 10);

        var cache = new ShipSpriteCache(folder);
        Assert.NotNull(cache.Get("CRUISER", 3, Direction.Horizontal));
        Assert.Equal(1, cache.LoadedCount);

        cache.Dispose();
        Assert.Equal(0, cache.LoadedCount);
        Assert.Null(cache.Get("CRUISER", 3, Direction.Horizontal));

        cache.Dispose(); // gọi lại không được lỗi
    });

    // ----- 20 ảnh thật đã được copy ra thư mục chạy -----

    private static (int Width, int Height) ExpectedSize(int length, Direction dir) =>
        dir == Direction.Horizontal ? (length * CellPixels, CellPixels) : (CellPixels, length * CellPixels);

    [Fact]
    public void ShippedAssets_CoverEveryShipAndDirection_WithTheCorrectSize()
    {
        using var cache = new ShipSpriteCache(); // Assets\Ships cạnh file chạy của project test
        var directions = new[] { Direction.Horizontal, Direction.Vertical };

        foreach (var spec in Fleet.Ships)
        {
            foreach (var dir in directions)
            {
                var image = cache.Get(spec.Name, spec.Length, dir);
                Assert.True(image is not null,
                    $"Thiếu ảnh {ShipSpriteNaming.NamedFile(spec.Name, dir)} tại {cache.FolderPath}. " +
                    "Kiểm tra mục Content/CopyToOutputDirectory trong csproj.");

                var (width, height) = ExpectedSize(spec.Length, dir);
                Assert.Equal(width, image!.Width);
                Assert.Equal(height, image.Height);
            }
        }
    }

    [Fact]
    public void ShippedAssets_IncludeTheGenericLengthImages_ForLengthOneToFive()
    {
        using var cache = new ShipSpriteCache();
        var directions = new[] { Direction.Horizontal, Direction.Vertical };

        for (var length = 1; length <= 5; length++)
        {
            foreach (var dir in directions)
            {
                // Tên không tồn tại nên phải rơi về ảnh theo độ dài.
                var image = cache.Get("KHONG_CO_TEN_NAY", length, dir);
                Assert.True(image is not null,
                    $"Thiếu ảnh {ShipSpriteNaming.LengthFile(length, dir)} tại {cache.FolderPath}.");

                var (width, height) = ExpectedSize(length, dir);
                Assert.Equal(width, image!.Width);
                Assert.Equal(height, image.Height);
            }
        }
    }
}

// =====================================================================================
// 2. BoardModel: lộ nguyên con tàu địch khi đã chìm
// =====================================================================================

public class BoardModelSunkShipTests
{
    [Fact]
    public void PlacementFromCells_HorizontalRun_StartsAtTheLeftmostCell()
    {
        var placement = BoardModel.PlacementFromCells("CRUISER", [new Cell(5, 2), new Cell(3, 2), new Cell(4, 2)]);

        Assert.NotNull(placement);
        Assert.Equal("CRUISER", placement!.Name);
        Assert.Equal(3, placement.X);
        Assert.Equal(2, placement.Y);
        Assert.Equal(Direction.Horizontal, placement.Dir);
    }

    [Fact]
    public void PlacementFromCells_VerticalRun_StartsAtTheTopCell()
    {
        var placement = BoardModel.PlacementFromCells("SUBMARINE", [new Cell(6, 8), new Cell(6, 6), new Cell(6, 7)]);

        Assert.NotNull(placement);
        Assert.Equal(6, placement!.X);
        Assert.Equal(6, placement.Y);
        Assert.Equal(Direction.Vertical, placement.Dir);
    }

    [Fact]
    public void PlacementFromCells_RejectsEmptyOrBentShapes()
    {
        Assert.Null(BoardModel.PlacementFromCells("CRUISER", []));
        Assert.Null(BoardModel.PlacementFromCells("CRUISER", [new Cell(1, 1), new Cell(2, 2)]));
    }

    [Fact]
    public void ApplyShot_SunkWithShipName_RevealsTheWholeHorizontalShip()
    {
        var board = new BoardModel();
        board.ApplyShot(3, 2, ShotResult.Hit);
        board.ApplyShot(5, 2, ShotResult.Sunk, [new Cell(3, 2), new Cell(4, 2), new Cell(5, 2)], "CRUISER");

        Assert.Equal(1, board.SunkShips.Count);
        Assert.Equal("CRUISER", board.SunkShips[0].Name);
        Assert.Equal(3, board.SunkShips[0].X);
        Assert.Equal(Direction.Horizontal, board.SunkShips[0].Dir);
        Assert.Equal(3, board.RevealedShips[0].Length);
        Assert.Equal(CellMark.Sunk, board.MarkAt(4, 2));
    }

    [Fact]
    public void ApplyShot_SunkWithShipName_RevealsTheWholeVerticalShip()
    {
        var board = new BoardModel();
        board.ApplyShot(6, 8, ShotResult.Sunk, [new Cell(6, 6), new Cell(6, 7), new Cell(6, 8)], "SUBMARINE");

        Assert.Equal(1, board.SunkShips.Count);
        Assert.Equal(6, board.SunkShips[0].X);
        Assert.Equal(6, board.SunkShips[0].Y);
        Assert.Equal(Direction.Vertical, board.SunkShips[0].Dir);
    }

    [Fact]
    public void ApplyShot_SunkWithoutName_MarksCellsButRevealsNoShip()
    {
        var board = new BoardModel();
        board.ApplyShot(1, 1, ShotResult.Sunk, [new Cell(1, 1), new Cell(2, 1)]);

        Assert.Equal(0, board.SunkShips.Count);
        Assert.Equal(CellMark.Sunk, board.MarkAt(2, 1));
    }

    [Fact]
    public void ApplyShot_HitOrMiss_NeverRevealsAShip()
    {
        var board = new BoardModel();
        board.ApplyShot(0, 0, ShotResult.Hit);
        board.ApplyShot(1, 1, ShotResult.Miss);

        Assert.Equal(0, board.SunkShips.Count); // sương mù: chưa chìm thì không lộ
    }

    [Fact]
    public void ApplyShot_SameShipSunkTwice_IsNotDuplicated()
    {
        var board = new BoardModel();
        board.ApplyShot(4, 2, ShotResult.Sunk, [new Cell(3, 2), new Cell(4, 2)], "DESTROYER");
        board.ApplyShot(4, 2, ShotResult.Sunk, [new Cell(3, 2), new Cell(4, 2)], "DESTROYER");

        Assert.Equal(1, board.SunkShips.Count);
    }

    [Fact]
    public void RevealSunkShip_ValidShip_MarksItsCellsAndRevealsIt()
    {
        var board = new BoardModel();

        Assert.True(board.RevealSunkShip(new ShipPlacement("DESTROYER", 7, 8, Direction.Horizontal)));
        Assert.Equal(1, board.SunkShips.Count);
        Assert.Equal(CellMark.Sunk, board.MarkAt(8, 8));
    }

    [Fact]
    public void RevealSunkShip_InvalidShip_IsRejected()
    {
        var board = new BoardModel();

        Assert.False(board.RevealSunkShip(new ShipPlacement("GUNBOAT", 0, 0, Direction.Horizontal)));
        Assert.False(board.RevealSunkShip(new ShipPlacement("CARRIER", 8, 0, Direction.Horizontal))); // ra ngoài bảng
        Assert.Equal(0, board.SunkShips.Count);
    }

    [Fact]
    public void IsShipSunk_IsTrueOnlyWhenEveryCellIsSunk()
    {
        var board = new BoardModel();
        var destroyer = new ShipPlacement("DESTROYER", 2, 2, Direction.Vertical);
        board.TryPlace(destroyer, out _);

        Assert.False(board.IsShipSunk(destroyer));

        board.ApplyShot(2, 2, ShotResult.Hit);
        Assert.False(board.IsShipSunk(destroyer));

        board.ApplyShot(2, 3, ShotResult.Sunk, [new Cell(2, 2), new Cell(2, 3)], "DESTROYER");
        Assert.True(board.IsShipSunk(destroyer));
    }

    [Fact]
    public void Reset_AlsoClearsTheRevealedShips()
    {
        var board = new BoardModel();
        board.ApplyShot(1, 0, ShotResult.Sunk, [new Cell(0, 0), new Cell(1, 0)], "DESTROYER");

        board.Reset();

        Assert.Equal(0, board.SunkShips.Count);
        Assert.Equal(CellMark.Empty, board.MarkAt(0, 0));
    }
}

// =====================================================================================
// 3. FormBattle: các bài cũ + hiển thị tàu địch chìm
// =====================================================================================

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

    /// <summary>Kết quả phát bắn không chìm tàu (next_turn và game_over không quan trọng với các bài này).</summary>
    private static FireResultInfo Shot(int x, int y, ShotResult result) =>
        new(x, y, result, null, [], Turn.You, false);

    /// <summary>Kết quả đánh chìm một tàu: phát bắn cuối rơi vào ô cuối cùng trong danh sách.</summary>
    private static FireResultInfo Sunk(string shipName, params Cell[] cells) =>
        new(cells[^1].X, cells[^1].Y, ShotResult.Sunk, shipName, cells, Turn.You, false);

    private static FormBattle NewForm() => new(DefaultFleet(), "Alice", "Bob") { SilentMode = true };

    [Fact]
    public void Initialization_ShowsFullFleetsAndLocksBoards() => Sta.Run(() =>
    {
        using var form = NewForm();

        Assert.Equal(5, form.MyRemainingShips);
        Assert.Equal(5, form.EnemyRemainingShips);
        Assert.False(form.IsGameOver);
        Assert.True(form.MyBoard.ShowShips);
        Assert.False(form.EnemyBoard.ShowShips);
        Assert.Equal(BoardMode.Disabled, form.MyBoard.Mode);
        Assert.Equal(BoardMode.Disabled, form.EnemyBoard.Mode);
    });

    [Fact]
    public void MyBoard_ShowsThePlacedShips() => Sta.Run(() =>
    {
        using var form = NewForm();
        Assert.Equal(5, form.MyBoard.PlacedShips.Count);
        Assert.True(form.MyBoard.AllShipsPlaced);
    });

    [Fact]
    public void TurnSwitching_UnlocksAndLocksTheEnemyBoard() => Sta.Run(() =>
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
    public void PlayerShotResults_UpdateTheEnemyBoardAndFleetCount() => Sta.Run(() =>
    {
        using var form = NewForm();

        form.ApplyMyShotResult(Shot(0, 0, ShotResult.Miss));
        Assert.True(form.EnemyBoard.IsAlreadyShot(0, 0));
        Assert.Equal(5, form.EnemyRemainingShips);

        form.ApplyMyShotResult(Shot(1, 1, ShotResult.Hit));
        Assert.True(form.EnemyBoard.IsAlreadyShot(1, 1));
        Assert.Equal(5, form.EnemyRemainingShips);

        form.ApplyMyShotResult(Sunk("DESTROYER", new Cell(2, 2), new Cell(3, 2)));
        Assert.Equal(4, form.EnemyRemainingShips);
    });

    [Fact]
    public void OpponentShotResults_UpdateMyBoardAndFleetCount() => Sta.Run(() =>
    {
        using var form = NewForm();

        form.ApplyOpponentShot(Shot(0, 0, ShotResult.Hit));
        Assert.True(form.MyBoard.IsAlreadyShot(0, 0));
        Assert.Equal(5, form.MyRemainingShips);

        form.ApplyOpponentShot(Sunk("DESTROYER", new Cell(0, 8), new Cell(1, 8)));
        Assert.Equal(4, form.MyRemainingShips);
    });

    [Fact]
    public void SinkingAllEnemyShips_EndsTheGameAsAWin() => Sta.Run(() =>
    {
        using var form = NewForm();

        for (var i = 0; i < 5; i++)
            form.ApplyMyShotResult(Sunk("DESTROYER", new Cell(i, 0)));

        Assert.Equal(0, form.EnemyRemainingShips);
        Assert.True(form.IsGameOver);
    });

    [Fact]
    public void LosingAllShips_EndsTheGameAsALoss() => Sta.Run(() =>
    {
        using var form = NewForm();

        for (var i = 0; i < 5; i++)
            form.ApplyOpponentShot(Sunk("DESTROYER", new Cell(i, 0)));

        Assert.Equal(0, form.MyRemainingShips);
        Assert.True(form.IsGameOver);
    });

    [Fact]
    public void EndGame_CanOnlyHappenOnce() => Sta.Run(() =>
    {
        using var form = NewForm();
        form.EndGame(isWinner: true, "thử");
        Assert.True(form.IsGameOver);

        form.EndGame(isWinner: false, "lần hai");
        Assert.True(form.IsGameOver);
    });

    // ----- Hiển thị tàu địch -----

    [Fact]
    public void EnemyBoard_StartsWithNoShipsAtAll_AndKeepsThemHidden() => Sta.Run(() =>
    {
        using var form = NewForm();

        Assert.False(form.EnemyBoard.ShowShips);
        Assert.Equal(0, form.EnemyBoard.PlacedShips.Count);
        Assert.Equal(0, form.EnemyBoard.SunkShips.Count);
    });

    [Fact]
    public void EnemyHit_WithoutSinking_RevealsNoShip() => Sta.Run(() =>
    {
        using var form = NewForm();

        form.ApplyMyShotResult(Shot(4, 4, ShotResult.Hit));

        Assert.Equal(0, form.EnemyBoard.SunkShips.Count);
    });

    [Fact]
    public void SunkEnemyShip_IsRevealedWhole_Horizontal() => Sta.Run(() =>
    {
        using var form = NewForm();

        form.ApplyMyShotResult(Sunk("CRUISER", new Cell(2, 1), new Cell(3, 1), new Cell(4, 1)));

        Assert.Equal(1, form.EnemyBoard.SunkShips.Count);
        var ship = form.EnemyBoard.SunkShips[0];
        Assert.Equal("CRUISER", ship.Name);
        Assert.Equal(2, ship.X);
        Assert.Equal(1, ship.Y);
        Assert.Equal(Direction.Horizontal, ship.Dir);
    });

    [Fact]
    public void SunkEnemyShip_IsRevealedWhole_Vertical() => Sta.Run(() =>
    {
        using var form = NewForm();

        form.ApplyMyShotResult(Sunk("BATTLESHIP", new Cell(7, 3), new Cell(7, 4), new Cell(7, 5), new Cell(7, 6)));

        var ship = form.EnemyBoard.SunkShips[0];
        Assert.Equal("BATTLESHIP", ship.Name);
        Assert.Equal(7, ship.X);
        Assert.Equal(3, ship.Y);
        Assert.Equal(Direction.Vertical, ship.Dir);
    });

    [Fact]
    public void OpponentSinksMyShip_MarksItsCellsOnMyBoard() => Sta.Run(() =>
    {
        using var form = NewForm();

        form.ApplyOpponentShot(Sunk("DESTROYER", new Cell(0, 8), new Cell(1, 8)));

        Assert.True(form.MyBoard.IsAlreadyShot(0, 8));
        Assert.True(form.MyBoard.IsAlreadyShot(1, 8));
        Assert.Equal(5, form.MyBoard.PlacedShips.Count); // tàu của mình vẫn còn trong danh sách để vẽ xác tàu
    });
}

// =====================================================================================
// 4. BoardPanel.Render: kiểm tra điểm ảnh (sương mù, dấu trúng/trượt đè lên tàu, fallback)
// =====================================================================================

public class BoardPanelRenderTests
{
    private const int LabelSpace = 18; // bằng hằng số cùng tên trong BoardPanel

    private static BoardPanel NewPanel() => new()
    {
        Size = new Size(340, 340),
        Mode = BoardMode.Target, // tránh lớp phủ mờ của chế độ Disabled
    };

    private static Bitmap RenderToBitmap(BoardPanel panel)
    {
        Assert.True(panel.Width > 0 && panel.Height > 0, "Bàn cờ phải có kích thước để vẽ");
        var bitmap = new Bitmap(panel.Width, panel.Height);
        using var g = Graphics.FromImage(bitmap);
        panel.Render(g, panel.Size);
        return bitmap;
    }

    /// <summary>Điểm ảnh ở chính giữa một ô của bàn cờ.</summary>
    private static Color CenterOf(BoardPanel panel, Bitmap bitmap, int cellX, int cellY)
    {
        var layout = BoardGeometry.Compute(panel.Width, panel.Height, panel.ShowCoordinates ? LabelSpace : 0);
        var x = layout.OriginX + cellX * layout.CellSize + layout.CellSize / 2;
        var y = layout.OriginY + cellY * layout.CellSize + layout.CellSize / 2;
        return bitmap.GetPixel(x, y);
    }

    private static string Describe(Color c) => $"({c.R},{c.G},{c.B})";

    private static void AssertSameColor(Color expected, Color actual, string what) =>
        Assert.True(expected.R == actual.R && expected.G == actual.G && expected.B == actual.B,
            $"{what}: cần {Describe(expected)} nhưng được {Describe(actual)}");

    private static void AssertDifferentColor(Color notExpected, Color actual, string what) =>
        Assert.False(notExpected.R == actual.R && notExpected.G == actual.G && notExpected.B == actual.B,
            $"{what}: không được là {Describe(notExpected)}");

    private static ShipPlacement Carrier() => new("CARRIER", 0, 3, Direction.Horizontal); // ô (0..4, 3)

    [Fact]
    public void OwnShip_IsDrawnOverTheWater() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.LoadShips([Carrier()]);
        using var bitmap = RenderToBitmap(panel);

        AssertDifferentColor(panel.WaterColor, CenterOf(panel, bitmap, 2, 3), "Giữa thân tàu");
        AssertSameColor(panel.WaterColor, CenterOf(panel, bitmap, 2, 6), "Ô nước trống");
    });

    [Fact]
    public void FogOfWar_ShipsAreNotDrawn_WhenShowShipsIsFalse() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.LoadShips([Carrier()]);
        panel.ShowShips = false; // như bảng địch
        using var bitmap = RenderToBitmap(panel);

        AssertSameColor(panel.WaterColor, CenterOf(panel, bitmap, 2, 3), "Tàu chưa chìm không được lộ");
    });

    [Fact]
    public void UnsunkEnemyShip_NeverAppears_OnTheTrackingBoard() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.ShowShips = false;
        panel.ApplyShot(2, 3, ShotResult.Hit); // trúng nhưng chưa chìm
        using var bitmap = RenderToBitmap(panel);

        AssertSameColor(panel.HitColor, CenterOf(panel, bitmap, 2, 3), "Dấu trúng");
        AssertSameColor(panel.WaterColor, CenterOf(panel, bitmap, 3, 3), "Ô kề bên không bị lộ tàu");
        AssertSameColor(panel.WaterColor, CenterOf(panel, bitmap, 1, 3), "Ô kề bên không bị lộ tàu");
    });

    [Fact]
    public void SunkEnemyShip_IsDrawn_OnTheTrackingBoard() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.ShowShips = false;
        panel.ApplyShot(3, 5, ShotResult.Sunk, [new Cell(1, 5), new Cell(2, 5), new Cell(3, 5)], "CRUISER");
        using var bitmap = RenderToBitmap(panel);

        for (var x = 1; x <= 3; x++)
            AssertDifferentColor(panel.WaterColor, CenterOf(panel, bitmap, x, 5), $"Ô chìm ({x},5)");
        AssertSameColor(panel.WaterColor, CenterOf(panel, bitmap, 4, 5), "Ô ngay sau tàu vẫn là nước");
    });

    [Fact]
    public void HitMarker_IsDrawnOnTopOfTheShip() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.LoadShips([Carrier()]);
        panel.ApplyShot(1, 3, ShotResult.Hit);
        using var bitmap = RenderToBitmap(panel);

        AssertSameColor(panel.HitColor, CenterOf(panel, bitmap, 1, 3), "Dấu trúng phải nằm trên ảnh tàu");
    });

    [Fact]
    public void MissMarker_IsDrawnOnTheWater() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.ApplyShot(6, 6, ShotResult.Miss);
        using var bitmap = RenderToBitmap(panel);

        AssertSameColor(panel.MissColor, CenterOf(panel, bitmap, 6, 6), "Dấu trượt");
    });

    [Fact]
    public void SunkOwnShip_IsMarkedAsSunk() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.LoadShips([Carrier()]);
        panel.ApplyShot(4, 3, ShotResult.Sunk,
            [new Cell(0, 3), new Cell(1, 3), new Cell(2, 3), new Cell(3, 3), new Cell(4, 3)], "CARRIER");
        using var bitmap = RenderToBitmap(panel);

        var center = CenterOf(panel, bitmap, 2, 3);
        // Có ảnh: dấu trúng nhỏ trên ảnh tối; không có ảnh: ô đỏ sẫm. Cả hai đều phải khác nước và khác màu tàu lành.
        AssertDifferentColor(panel.WaterColor, center, "Tàu chìm");
        AssertDifferentColor(panel.ShipColor, center, "Tàu chìm phải khác tàu lành");
    });

    [Fact]
    public void UseShipImagesFalse_DrawsThePlainFallbackBlock() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.UseShipImages = false;
        panel.LoadShips([Carrier()]);
        using var bitmap = RenderToBitmap(panel);

        AssertSameColor(panel.ShipColor, CenterOf(panel, bitmap, 2, 3), "Khối màu thay thế");
    });

    [Fact]
    public void MissingSpriteFolder_FallsBackToBlocks_WithoutThrowing() => Sta.Run(() =>
    {
        var missing = Path.Combine(Path.GetTempPath(), "battleship_khong_co_" + Guid.NewGuid().ToString("N"));
        using var cache = new ShipSpriteCache(missing);
        using var panel = NewPanel();
        panel.Sprites = cache;
        panel.LoadShips([Carrier()]);

        using var bitmap = RenderToBitmap(panel); // không được ném lỗi

        AssertSameColor(panel.ShipColor, CenterOf(panel, bitmap, 2, 3), "Thiếu ảnh thì phải vẽ khối màu");
    });

    [Fact]
    public void DisabledMode_DimsTheBoard() => Sta.Run(() =>
    {
        using var panel = NewPanel();
        panel.Mode = BoardMode.Disabled;
        using var bitmap = RenderToBitmap(panel);

        var water = CenterOf(panel, bitmap, 8, 8);
        Assert.True(water.R < panel.WaterColor.R || water.G < panel.WaterColor.G || water.B < panel.WaterColor.B,
            "Chế độ Disabled phải làm tối bàn cờ");
    });

    [Fact]
    public void FormBattle_EnemyBoardShowsNothingWhereMyShipsAre() => Sta.Run(() =>
    {
        using var form = new FormBattle(
            [new("CARRIER", 0, 0, Direction.Horizontal), new("BATTLESHIP", 0, 2, Direction.Horizontal),
             new("CRUISER", 0, 4, Direction.Horizontal), new("SUBMARINE", 0, 6, Direction.Horizontal),
             new("DESTROYER", 0, 8, Direction.Horizontal)],
            "Alice", "Bob")
        { SilentMode = true };

        using var mine = RenderToBitmap(form.MyBoard);
        using var enemy = RenderToBitmap(form.EnemyBoard);

        // So với một ô nước trống trên CÙNG bàn (cả hai đều đang bị làm mờ như nhau).
        AssertDifferentColor(CenterOf(form.MyBoard, mine, 9, 9), CenterOf(form.MyBoard, mine, 2, 0), "Bàn của mình thấy tàu");
        AssertSameColor(CenterOf(form.EnemyBoard, enemy, 9, 9), CenterOf(form.EnemyBoard, enemy, 2, 0), "Bàn địch không được thấy tàu");
    });
}