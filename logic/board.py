from __future__ import annotations

import random
from dataclasses import dataclass
from typing import Any

from .exceptions import AlreadyFiredError, InvalidShipsError, NotPlacedError, OutOfBoundsError
from .ship import Cell, SHIP_SPECS, Ship

BOARD_SIZE = 10

# Trạng thái ô bàn cờ (mục 3.4 PROTOCOL.md)
EMPTY = "EMPTY"
SHIP = "SHIP"
HIT = "HIT"
MISS = "MISS"


@dataclass
class ShotResult:
    """Kết quả phát bắn tại một ô."""

    result: str  # "MISS" | "HIT" | "SUNK"
    sunk_ship: str | None
    sunk_cells: list[dict[str, int]]
    x: int
    y: int

    def to_dict(self) -> dict[str, Any]:
        return {
            "result": self.result,
            "sunk_ship": self.sunk_ship,
            "sunk_cells": self.sunk_cells,
            "x": self.x,
            "y": self.y,
        }


class Board:
    """
    Bàn cờ 10x10 lưu giữ các tàu, trạng thái bắn và tính toán kết quả bắn.
    """

    def __init__(self) -> None:
        self.ships: dict[str, Ship] = {}
        self.occupied: dict[Cell, Ship] = {}
        self.fired_cells: set[Cell] = set()
        self.is_placed: bool = False

    @staticmethod
    def validate_ships(placements: Any) -> list[Ship]:
        """
        Kiểm tra tính hợp lệ của đội tàu theo mục 3.3 PROTOCOL.md:
        1. Đủ đúng 5 tàu, đủ 5 tên (CARRIER, BATTLESHIP, CRUISER, SUBMARINE, DESTROYER), không trùng tên.
        2. Không có ô nào nằm ngoài bảng (0..9).
        3. Không có hai tàu chồng lên cùng một ô.
        4. Tàu được phép đặt sát nhau (chạm cạnh hoặc góc).
        """
        raw_list = placements
        if isinstance(raw_list, dict) and "ships" in raw_list:
            raw_list = raw_list["ships"]

        if not isinstance(raw_list, (list, tuple)):
            raise InvalidShipsError("Danh sách tàu phải là dạng list/array")

        if len(raw_list) != len(SHIP_SPECS):
            raise InvalidShipsError(f"Cần đặt đúng {len(SHIP_SPECS)} tàu, nhận được {len(raw_list)}")

        parsed_ships: list[Ship] = []
        for item in raw_list:
            if isinstance(item, Ship):
                ship = item
            elif isinstance(item, dict):
                ship = Ship.from_dict(item)
            else:
                raise InvalidShipsError("Mỗi tàu phải là object/dict hoặc instance của Ship")
            parsed_ships.append(ship)

        names_seen = set()
        occupied_cells: dict[Cell, str] = {}

        for ship in parsed_ships:
            if ship.name in names_seen:
                raise InvalidShipsError(f"Tàu {ship.name} bị đặt trùng tên")
            names_seen.add(ship.name)

            for cell in ship.cells:
                if not (0 <= cell.x < BOARD_SIZE and 0 <= cell.y < BOARD_SIZE):
                    raise InvalidShipsError(
                        f"Tàu {ship.name} nằm ngoài bảng tại ô ({cell.x}, {cell.y})"
                    )

                if cell in occupied_cells:
                    other_name = occupied_cells[cell]
                    raise InvalidShipsError(
                        f"Tàu {ship.name} chồng lên tàu {other_name} tại ô ({cell.x}, {cell.y})"
                    )

                occupied_cells[cell] = ship.name

        missing_names = set(SHIP_SPECS.keys()) - names_seen
        if missing_names:
            raise InvalidShipsError(f"Thiếu các loại tàu bắt buộc: {', '.join(sorted(missing_names))}")

        return parsed_ships

    def place_ships(self, placements: Any) -> None:
        """Đặt tàu lên bàn cờ sau khi kiểm tra hợp lệ."""
        valid_ships = self.validate_ships(placements)

        self.ships.clear()
        self.occupied.clear()
        self.fired_cells.clear()

        for ship in valid_ships:
            self.ships[ship.name] = ship
            for cell in ship.cells:
                self.occupied[cell] = ship

        self.is_placed = True

    def fire(self, x: int, y: int) -> ShotResult:
        """
        Bắn một phát vào ô (x, y).
        Trả về ShotResult (MISS, HIT, SUNK).
        Ném OutOfBoundsError nếu ra ngoài bảng.
        Ném AlreadyFiredError nếu ô đã bắn rồi.
        """
        if not self.is_placed:
            raise NotPlacedError("Chưa đặt tàu trên bàn cờ")

        try:
            x_int = int(x)
            y_int = int(y)
        except (ValueError, TypeError) as exc:
            raise OutOfBoundsError("Tọa độ bắn phải là số nguyên") from exc

        if not (0 <= x_int < BOARD_SIZE and 0 <= y_int < BOARD_SIZE):
            raise OutOfBoundsError(f"Tọa độ ({x}, {y}) ngoài bảng 0..{BOARD_SIZE - 1}")

        target = Cell(x_int, y_int)
        if target in self.fired_cells:
            raise AlreadyFiredError(f"Ô ({x_int}, {y_int}) đã bắn rồi")

        self.fired_cells.add(target)

        if target in self.occupied:
            ship = self.occupied[target]
            ship.hit(x_int, y_int)

            if ship.is_sunk:
                sunk_cells = [c.to_dict() for c in ship.cells]
                return ShotResult(
                    result="SUNK",
                    sunk_ship=ship.name,
                    sunk_cells=sunk_cells,
                    x=x_int,
                    y=y_int,
                )
            return ShotResult(result="HIT", sunk_ship=None, sunk_cells=[], x=x_int, y=y_int)

        return ShotResult(result="MISS", sunk_ship=None, sunk_cells=[], x=x_int, y=y_int)

    @property
    def all_sunk(self) -> bool:
        """Kiểm tra toàn bộ 5 tàu đã bị bắn chìm hay chưa."""
        return self.is_placed and len(self.ships) == 5 and all(s.is_sunk for s in self.ships.values())

    @property
    def ships_remaining(self) -> int:
        """Số tàu chưa bị bắn chìm."""
        return sum(1 for s in self.ships.values() if not s.is_sunk)

    @property
    def shots_fired(self) -> int:
        """Tổng số phát bắn đã thực hiện trên bàn cờ này."""
        return len(self.fired_cells)

    def is_already_fired(self, x: int, y: int) -> bool:
        """Kiểm tra ô (x, y) đã từng bị bắn chưa."""
        return Cell(x, y) in self.fired_cells

    def get_cell_state(self, x: int, y: int, show_ships: bool = True) -> str:
        """Lấy trạng thái ô (EMPTY, SHIP, HIT, MISS)."""
        cell = Cell(x, y)
        if cell in self.fired_cells:
            return HIT if cell in self.occupied else MISS
        if show_ships and cell in self.occupied:
            return SHIP
        return EMPTY

    def get_owner_view(self) -> list[list[str]]:
        """Hiển thị toàn bộ bảng của chủ sở hữu (thấy tàu của mình, các ô hit/miss)."""
        return [
            [self.get_cell_state(x, y, show_ships=True) for x in range(BOARD_SIZE)]
            for y in range(BOARD_SIZE)
        ]

    def get_tracking_view(self) -> list[list[str]]:
        """Bảng theo dõi của đối thủ (chỉ thấy HIT, MISS, EMPTY; không thấy tàu chưa trúng)."""
        return [
            [self.get_cell_state(x, y, show_ships=False) for x in range(BOARD_SIZE)]
            for y in range(BOARD_SIZE)
        ]

    @classmethod
    def generate_random_fleet(cls, rng: random.Random | None = None) -> list[dict[str, Any]]:
        """Sinh ngẫu nhiên một danh sách 5 tàu hợp lệ."""
        r = rng if rng is not None else random.Random()
        placements: list[dict[str, Any]] = []
        occupied: set[Cell] = set()

        for name, length in SHIP_SPECS.items():
            placed = False
            attempts = 0
            while not placed and attempts < 1000:
                attempts += 1
                dir_ = r.choice(["H", "V"])
                if dir_ == "H":
                    x = r.randint(0, BOARD_SIZE - length)
                    y = r.randint(0, BOARD_SIZE - 1)
                    cells = [Cell(x + i, y) for i in range(length)]
                else:
                    x = r.randint(0, BOARD_SIZE - 1)
                    y = r.randint(0, BOARD_SIZE - length)
                    cells = [Cell(x, y + i) for i in range(length)]

                if any(c in occupied for c in cells):
                    continue

                for c in cells:
                    occupied.add(c)
                placements.append({"name": name, "x": x, "y": y, "dir": dir_})
                placed = True

            if not placed:
                # Thử lại từ đầu nếu gặp kẹt hiếm hoi
                return cls.generate_random_fleet(r)

        return placements
