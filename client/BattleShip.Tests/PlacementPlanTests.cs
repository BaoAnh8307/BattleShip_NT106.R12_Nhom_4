using BattleShip.Forms;
using BattleShip.Network;

namespace BattleShip.Tests;

/// <summary>Kiểm thử logic chọn tàu kế tiếp và chữ trên danh sách tàu của màn đặt tàu.</summary>
public class PlacementPlanTests
{
    private static ShipPlacement Placed(string name) => new(name, 0, 0, Direction.Horizontal);

    [Fact]
    public void NextUnplaced_StartsWithFirstShipOfTheFleet()
    {
        var next = PlacementPlan.NextUnplaced([]);
        Assert.NotNull(next);
        Assert.Equal(Fleet.Ships[0].Name, next!.Name);
    }

    [Fact]
    public void NextUnplaced_SkipsShipsAlreadyPlaced()
    {
        var next = PlacementPlan.NextUnplaced([Placed("CARRIER")]);
        Assert.Equal("BATTLESHIP", next!.Name);
    }

    [Fact]
    public void NextUnplaced_FindsTheGapWhenShipsArePlacedOutOfOrder()
    {
        var next = PlacementPlan.NextUnplaced([Placed("CARRIER"), Placed("CRUISER")]);
        Assert.Equal("BATTLESHIP", next!.Name);
    }

    [Fact]
    public void NextUnplaced_ReturnsNullWhenAllShipsPlaced()
    {
        var all = Fleet.Ships.Select(s => Placed(s.Name)).ToList();
        Assert.Null(PlacementPlan.NextUnplaced(all));
    }

    [Fact]
    public void Label_ShowsNameAndLength_AndMarksPlacedShips()
    {
        var carrier = Fleet.Find("CARRIER")!;
        Assert.Equal("Tàu sân bay (5 ô)", PlacementPlan.Label(carrier, false));
        Assert.Equal("✓  Tàu sân bay (5 ô)", PlacementPlan.Label(carrier, true));
    }
}
