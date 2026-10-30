namespace BattleShip.Network;

/// <summary>Bảng giả dùng nội bộ trong MockNetworkClient: giữ tàu, các ô đã bắn, tính kết quả.</summary>
internal sealed class MockBoard
{
    private sealed class PlacedShip(string name, IReadOnlyList<Cell> cells)
    {
        public string Name { get; } = name;
        public IReadOnlyList<Cell> Cells { get; } = cells;
        public int Hits { get; set; }
        public bool IsSunk => Hits >= Cells.Count;
    }

    private readonly List<PlacedShip> _ships = [];
    private readonly Dictionary<Cell, PlacedShip> _owner = [];
    private readonly HashSet<Cell> _fired = [];

    public MockBoard(IEnumerable<ShipPlacement> placements)
    {
        foreach (var placement in placements)
        {
            var ship = new PlacedShip(placement.Name, Fleet.CellsOf(placement));
            _ships.Add(ship);
            foreach (var cell in ship.Cells)
                _owner[cell] = ship;
        }
    }

    public int ShotsFired => _fired.Count;

    public bool AllSunk => _ships.All(s => s.IsSunk);

    public bool AlreadyFired(Cell cell) => _fired.Contains(cell);

    public (ShotResult Result, string? SunkShip, IReadOnlyList<Cell> SunkCells) Fire(Cell cell)
    {
        _fired.Add(cell);

        if (!_owner.TryGetValue(cell, out var ship))
            return (ShotResult.Miss, null, []);

        ship.Hits++;
        return ship.IsSunk
            ? (ShotResult.Sunk, ship.Name, ship.Cells)
            : (ShotResult.Hit, null, []);
    }

    public Cell PickRandomUnfired(Random rng)
    {
        var candidates = new List<Cell>();
        for (var y = 0; y < Fleet.BoardSize; y++)
            for (var x = 0; x < Fleet.BoardSize; x++)
                if (!_fired.Contains(new Cell(x, y)))
                    candidates.Add(new Cell(x, y));

        return candidates[rng.Next(candidates.Count)];
    }

    public static MockBoard CreateRandom(Random rng)
    {
        var placements = new List<ShipPlacement>();
        var used = new HashSet<Cell>();

        foreach (var spec in Fleet.Ships)
        {
            while (true)
            {
                var dir = rng.Next(2) == 0 ? Direction.Horizontal : Direction.Vertical;
                var placement = new ShipPlacement(
                    spec.Name, rng.Next(Fleet.BoardSize), rng.Next(Fleet.BoardSize), dir);
                var cells = Fleet.CellsOf(placement);

                if (cells.Any(c => !Fleet.InBounds(c.X, c.Y) || used.Contains(c)))
                    continue;

                foreach (var cell in cells)
                    used.Add(cell);
                placements.Add(placement);
                break;
            }
        }
        return new MockBoard(placements);
    }
}
