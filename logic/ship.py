from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from .exceptions import InvalidShipsError

SHIP_SPECS: dict[str, int] = {
    "CARRIER": 5,
    "BATTLESHIP": 4,
    "CRUISER": 3,
    "SUBMARINE": 3,
    "DESTROYER": 2,
}

SHIP_DISPLAY_NAMES: dict[str, str] = {
    "CARRIER": "Tàu sân bay",
    "BATTLESHIP": "Thiết giáp hạm",
    "CRUISER": "Tàu tuần dương",
    "SUBMARINE": "Tàu ngầm",
    "DESTROYER": "Tàu khu trục",
}


@dataclass(frozen=True)
class Cell:
    """Đại diện một tọa độ trên bàn cờ. x là cột (0..9), y là hàng (0..9)."""

    x: int
    y: int

    def __iter__(self):
        yield self.x
        yield self.y

    def __getitem__(self, item: Any) -> int:
        if item in (0, "x", "X"):
            return self.x
        if item in (1, "y", "Y"):
            return self.y
        raise KeyError(f"Invalid cell index or key: {item}")

    def to_dict(self) -> dict[str, int]:
        return {"x": self.x, "y": self.y}


@dataclass
class Ship:
    """
    Thông tin một tàu được đặt trên bàn cờ.
    - name: Tên tàu (CARRIER, BATTLESHIP, CRUISER, SUBMARINE, DESTROYER).
    - x, y: Ô đầu (trái nhất với H, trên cùng với V).
    - dir: Hướng đặt, 'H' (ngang) hoặc 'V' (dọc).
    - length: Độ dài tương ứng với tên tàu.
    """

    name: str
    x: int
    y: int
    dir: str
    length: int = field(init=False)
    cells: list[Cell] = field(init=False)
    hits: set[Cell] = field(default_factory=set, init=False)

    def __post_init__(self) -> None:
        self.name = str(self.name).upper().strip()
        if self.name not in SHIP_SPECS:
            raise InvalidShipsError(f"Tên tàu không hợp lệ: {self.name}")

        self.length = SHIP_SPECS[self.name]

        dir_str = str(self.dir).upper().strip()
        if dir_str not in ("H", "V"):
            raise InvalidShipsError(f"Hướng đặt tàu phải là 'H' hoặc 'V', nhận được: {self.dir}")
        self.dir = dir_str

        try:
            self.x = int(self.x)
            self.y = int(self.y)
        except (ValueError, TypeError) as exc:
            raise InvalidShipsError("Tọa độ tàu phải là số nguyên") from exc

        if self.dir == "H":
            self.cells = [Cell(self.x + i, self.y) for i in range(self.length)]
        else:
            self.cells = [Cell(self.x, self.y + i) for i in range(self.length)]

    @property
    def is_sunk(self) -> bool:
        """Tàu bị chìm khi toàn bộ các ô của tàu đã bị bắn trúng."""
        return len(self.hits) >= self.length

    def hit(self, x: int, y: int) -> bool:
        """Đánh dấu một ô của tàu bị bắn trúng nếu tọa độ thuộc tàu. Trả về True nếu trúng."""
        target = Cell(x, y)
        if target in self.cells:
            self.hits.add(target)
            return True
        return False

    def to_dict(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "x": self.x,
            "y": self.y,
            "dir": self.dir,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Ship:
        if not isinstance(data, dict):
            raise InvalidShipsError("Dữ liệu vị trí tàu phải là một object/dict")
        for key in ("name", "x", "y", "dir"):
            if key not in data:
                raise InvalidShipsError(f"Thiếu trường '{key}' trong thông tin tàu")
        return cls(name=data["name"], x=data["x"], y=data["y"], dir=data["dir"])
