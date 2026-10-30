import pytest

from logic.exceptions import InvalidShipsError
from logic.ship import Cell, SHIP_SPECS, Ship


def test_cell_behavior():
    c = Cell(3, 7)
    assert c.x == 3
    assert c.y == 7
    # unpackable
    x, y = c
    assert (x, y) == (3, 7)
    # subscriptable
    assert c[0] == 3
    assert c[1] == 7
    assert c["x"] == 3
    assert c["y"] == 7
    with pytest.raises(KeyError):
        _ = c[2]
    with pytest.raises(KeyError):
        _ = c["z"]
    # to_dict
    assert c.to_dict() == {"x": 3, "y": 7}
    # hashable in set
    s = {c, Cell(3, 7), Cell(4, 7)}
    assert len(s) == 2


def test_ship_creation_horizontal():
    carrier = Ship(name="CARRIER", x=0, y=2, dir="H")
    assert carrier.name == "CARRIER"
    assert carrier.length == 5
    assert carrier.cells == [Cell(0, 2), Cell(1, 2), Cell(2, 2), Cell(3, 2), Cell(4, 2)]
    assert not carrier.is_sunk


def test_ship_creation_vertical():
    sub = Ship(name="SUBMARINE", x=5, y=3, dir="V")
    assert sub.name == "SUBMARINE"
    assert sub.length == 3
    assert sub.cells == [Cell(5, 3), Cell(5, 4), Cell(5, 5)]
    assert not sub.is_sunk


def test_ship_lengths_match_specs():
    for name, expected_len in SHIP_SPECS.items():
        ship = Ship(name=name, x=0, y=0, dir="H")
        assert ship.length == expected_len
        assert len(ship.cells) == expected_len


def test_ship_invalid_name():
    with pytest.raises(InvalidShipsError):
        Ship(name="PIRATE_SHIP", x=0, y=0, dir="H")


def test_ship_invalid_dir():
    with pytest.raises(InvalidShipsError):
        Ship(name="CARRIER", x=0, y=0, dir="DIAGONAL")


def test_ship_invalid_coords():
    with pytest.raises(InvalidShipsError):
        Ship(name="CARRIER", x="abc", y=0, dir="H")


def test_ship_hit_and_sink():
    destroyer = Ship(name="DESTROYER", x=1, y=1, dir="H")
    assert destroyer.length == 2

    # Bắn trượt tàu
    assert not destroyer.hit(0, 0)
    assert len(destroyer.hits) == 0
    assert not destroyer.is_sunk

    # Bắn trúng ô đầu
    assert destroyer.hit(1, 1)
    assert len(destroyer.hits) == 1
    assert not destroyer.is_sunk

    # Bắn lại ô cũ (vẫn trúng nhưng set không tăng size)
    assert destroyer.hit(1, 1)
    assert len(destroyer.hits) == 1

    # Bắn trúng ô cuối -> Chìm
    assert destroyer.hit(2, 1)
    assert len(destroyer.hits) == 2
    assert destroyer.is_sunk


def test_ship_serialization():
    data = {"name": "BATTLESHIP", "x": 2, "y": 4, "dir": "V"}
    ship = Ship.from_dict(data)
    assert ship.name == "BATTLESHIP"
    assert ship.length == 4
    assert ship.to_dict() == data


def test_ship_from_dict_invalid():
    with pytest.raises(InvalidShipsError):
        Ship.from_dict("not-a-dict")
    with pytest.raises(InvalidShipsError):
        Ship.from_dict({"name": "BATTLESHIP", "x": 0, "y": 0})
