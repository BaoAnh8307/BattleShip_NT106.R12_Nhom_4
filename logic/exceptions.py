from __future__ import annotations


class LogicError(Exception):
    """Lỗi nghiệp vụ của logic game Battleship, có thuộc tính code và message."""

    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code
        self.message = message

    def __repr__(self) -> str:
        return f"{self.__class__.__name__}(code={self.code!r}, message={self.message!r})"


class InvalidShipsError(LogicError):
    """Vị trí hoặc danh sách tàu không hợp lệ (mục 3.3 PROTOCOL.md)."""

    def __init__(self, message: str = "Vị trí tàu không hợp lệ"):
        super().__init__("INVALID_SHIPS", message)


class NotPlacedError(LogicError):
    """Client bấm READY khi chưa đặt tàu hợp lệ."""

    def __init__(self, message: str = "Chưa đặt tàu hợp lệ"):
        super().__init__("NOT_PLACED", message)


class NotYourTurnError(LogicError):
    """Client gửi FIRE khi chưa đến lượt."""

    def __init__(self, message: str = "Chưa đến lượt của bạn"):
        super().__init__("NOT_YOUR_TURN", message)


class OutOfBoundsError(LogicError):
    """Tọa độ bắn ngoài bảng 0-9."""

    def __init__(self, message: str = "Tọa độ bắn ngoài bảng 0–9"):
        super().__init__("OUT_OF_BOUNDS", message)


class AlreadyFiredError(LogicError):
    """Ô này đã được bắn trước đó."""

    def __init__(self, message: str = "Ô này đã bắn rồi"):
        super().__init__("ALREADY_FIRED", message)


class BadStateError(LogicError):
    """Hành động không hợp lệ ở trạng thái ván đấu hiện tại."""

    def __init__(self, message: str = "Trạng thái ván đấu không hợp lệ cho hành động này"):
        super().__init__("BAD_STATE", message)


class InvalidParamsError(LogicError):
    """Tham số truyền vào không hợp lệ."""

    def __init__(self, message: str = "Tham số không hợp lệ"):
        super().__init__("INVALID_PARAMS", message)
