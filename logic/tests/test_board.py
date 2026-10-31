import pytest

from logic.board import Board, EMPTY, HIT, MISS, SHIP, ShotResult
from logic.exceptions import AlreadyFiredError, InvalidShipsError, NotPlacedError, OutOfBoundsError
from logic.ship import Cell, Ship


def sample_valid_fleet():
    return [
        {"name": "CARRIER", "x": 0, "y": 0, "dir": "H"},      # (0..4, 0)
        {"name": "BATTLESHIP", "x": 0, "y": 1, "dir": "H"},   # (0..3, 1) - sát nhau
        {"name": "CRUISER", "x": 0, "y": 2, "dir": "H"},      # (0..2, 2)
        {"name": "SUBMARINE", "x": 0, "y": 3, "dir": "H"},    # (0..2, 3)
        {"name": "DESTROYER", "x": 0, "y": 4, "dir": "H"},    # (0..1, 4)
    ]


def test_shot_result_to_dict():
    res = ShotResult("HIT", None, [], 3, 5)
    assert res.to_dict() == {
        "result": "HIT",
        "sunk_ship": None,
        "sunk_cells": [],
        "x": 3,
        "y": 5,
    }


def test_board_valid_placement():
    board = Board()
    assert not board.is_placed
    board.place_ships(sample_valid_fleet())
    assert board.is_placed
    assert len(board.ships) == 5
    assert board.ships_remaining == 5
    assert not board.all_sunk


def test_board_place_with_dict_wrapper_and_instances():
    board = Board()
    # Gói trong {"ships": [...]} và dùng instances của Ship
    fleet_objs = [Ship.from_dict(d) for d in sample_valid_fleet()]
    board.place_ships({"ships": fleet_objs})
    assert board.is_placed
    assert len(board.ships) == 5


def test_board_place_invalid_collection():
    board = Board()
    with pytest.raises(InvalidShipsError):
        board.place_ships("invalid")
    with pytest.raises(InvalidShipsError):
        board.place_ships([123, 456])


def test_board_adjacent_ships_allowed():
    # Luật: Tàu được phép đặt sát nhau (mục 3.3 PROTOCOL.md)
    board = Board()
    board.place_ships(sample_valid_fleet())
    assert board.is_placed


def test_board_reject_overlapping_ships():
    fleet = sample_valid_fleet()
    # Tàu BATTLESHIP đặt dọc cắt qua CARRIER tại (0,0)
    fleet[1] = {"name": "BATTLESHIP", "x": 0, "y": 0, "dir": "V"}
    board = Board()
    with pytest.raises(InvalidShipsError, match="chồng lên"):
        board.place_ships(fleet)


def test_board_reject_out_of_bounds_placement():
    fleet = sample_valid_fleet()
    # CARRIER dài 5, x=7 -> 7..11 ra ngoài bảng
    fleet[0] = {"name": "CARRIER", "x": 7, "y": 0, "dir": "H"}
    board = Board()
    with pytest.raises(InvalidShipsError, match="ngoài bảng"):
        board.place_ships(fleet)

    # Tọa độ âm
    fleet[0] = {"name": "CARRIER", "x": -1, "y": 0, "dir": "H"}
    with pytest.raises(InvalidShipsError):
        board.place_ships(fleet)


def test_board_reject_missing_or_duplicate_ships():
    board = Board()
    # Thiếu tàu (chỉ có 4 tàu)
    with pytest.raises(InvalidShipsError):
        board.place_ships(sample_valid_fleet()[:4])

    # Trùng tên tàu (2 CARRIER, thiếu DESTROYER)
    duplicate_fleet = sample_valid_fleet()
    duplicate_fleet[4] = {"name": "CARRIER", "x": 0, "y": 9, "dir": "H"}
    with pytest.raises(InvalidShipsError):
        board.place_ships(duplicate_fleet)


def test_fire_before_placed():
    board = Board()
    with pytest.raises(NotPlacedError):
        board.fire(0, 0)


def test_fire_out_of_bounds():
    board = Board()
    board.place_ships(sample_valid_fleet())

    with pytest.raises(OutOfBoundsError):
        board.fire("invalid", 0)
    with pytest.raises(OutOfBoundsError):
        board.fire(-1, 0)
    with pytest.raises(OutOfBoundsError):
        board.fire(10, 0)
    with pytest.raises(OutOfBoundsError):
        board.fire(0, 10)
    with pytest.raises(OutOfBoundsError):
        board.fire(0, -1)


def test_fire_miss():
    board = Board()
    board.place_ships(sample_valid_fleet())

    assert not board.is_already_fired(9, 9)
    res = board.fire(9, 9)
    assert res.result == "MISS"
    assert res.sunk_ship is None
    assert res.sunk_cells == []
    assert res.x == 9 and res.y == 9
    assert board.get_cell_state(9, 9) == MISS
    assert board.shots_fired == 1
    assert board.is_already_fired(9, 9)


def test_fire_already_fired():
    board = Board()
    board.place_ships(sample_valid_fleet())

    board.fire(9, 9)
    with pytest.raises(AlreadyFiredError):
        board.fire(9, 9)

    assert board.shots_fired == 1


def test_fire_hit_and_sink():
    board = Board()
    board.place_ships(sample_valid_fleet())
    # DESTROYER đặt tại (0, 4) và (1, 4)

    # Bắn trúng phát 1
    res1 = board.fire(0, 4)
    assert res1.result == "HIT"
    assert res1.sunk_ship is None
    assert res1.sunk_cells == []
    assert board.get_cell_state(0, 4) == HIT
    assert board.ships_remaining == 5

    # Bắn trúng phát 2 -> Chìm DESTROYER
    res2 = board.fire(1, 4)
    assert res2.result == "SUNK"
    assert res2.sunk_ship == "DESTROYER"
    assert res2.sunk_cells == [{"x": 0, "y": 4}, {"x": 1, "y": 4}]
    assert board.ships_remaining == 4
    assert not board.all_sunk


def test_sink_all_ships():
    board = Board()
    board.place_ships(sample_valid_fleet())

    # Bắn chìm toàn bộ các ô của cả 5 tàu
    for ship in board.ships.values():
        for cell in ship.cells:
            board.fire(cell.x, cell.y)

    assert board.all_sunk
    assert board.ships_remaining == 0


def test_views():
    board = Board()
    board.place_ships(sample_valid_fleet())

    # Chưa bắn:
    # owner view thấy SHIP tại (0, 0)
    assert board.get_owner_view()[0][0] == SHIP
    # tracking view của đối thủ chỉ thấy EMPTY
    assert board.get_tracking_view()[0][0] == EMPTY

    # Sau khi bắn trúng:
    board.fire(0, 0)
    assert board.get_owner_view()[0][0] == HIT
    assert board.get_tracking_view()[0][0] == HIT

    # Bắn trượt:
    board.fire(9, 9)
    assert board.get_owner_view()[9][9] == MISS
    assert board.get_tracking_view()[9][9] == MISS


def test_generate_random_fleet():
    board = Board()
    fleet = Board.generate_random_fleet()
    board.place_ships(fleet)
    assert board.is_placed
    assert len(board.ships) == 5

def test_board_edge_cases():
    board = Board()
    # 5 items but not dict or Ship
    with pytest.raises(InvalidShipsError, match="Mỗi tàu phải là"):
        board.place_ships([1, 2, 3, 4, 5])

    # Run random fleet multiple times to cover both H and V orientations
    for _ in range(5):
        fleet = Board.generate_random_fleet()
        b = Board()
        b.place_ships(fleet)
        assert b.is_placed
