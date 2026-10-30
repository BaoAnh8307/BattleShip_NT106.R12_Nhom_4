from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import random
import threading
from typing import Any

from .board import Board
from .exceptions import BadStateError, LogicError, NotPlacedError, NotYourTurnError


@dataclass
class FireResult:
    """
    Kết quả lượt bắn từ logic game trả về cho server:
    Khớp với hợp đồng mục 8.1 PROTOCOL.md.
    """

    x: int
    y: int
    result: str  # "MISS" | "HIT" | "SUNK"
    sunk_ship: str | None
    sunk_cells: list[dict[str, int]]
    next_turn: Any | None  # player_id của người tiếp theo, hoặc None khi game_over
    game_over: bool
    shooter: Any
    target: Any
    winner: Any | None = None
    loser: Any | None = None
    end_reason: str | None = None
    total_shots: int = 0

    def for_player(self, player_id: Any) -> dict[str, Any]:
        """
        Dịch kết quả sang định dạng gói tin mạng (FIRE_RESULT hoặc OPPONENT_FIRED)
        tính riêng theo người nhận: next_turn là 'you' hoặc 'opponent'.
        """
        if self.game_over:
            turn_str = None
        elif self.next_turn == player_id:
            turn_str = "you"
        else:
            turn_str = "opponent"

        return {
            "x": self.x,
            "y": self.y,
            "result": self.result,
            "sunk_ship": self.sunk_ship,
            "sunk_cells": self.sunk_cells,
            "next_turn": turn_str,
            "game_over": self.game_over,
        }


class GameSession:
    """
    Quản lý trạng thái và luật chơi của một ván đấu giữa 2 người chơi.
    Sử dụng Lock để đảm bảo an toàn đa luồng (thread-safe) theo mục 8.3 PROTOCOL.md.
    """

    STATE_PLACING = "PLACING"
    STATE_PLAYING = "PLAYING"
    STATE_FINISHED = "FINISHED"
    STATE_CANCELLED = "CANCELLED"

    def __init__(
        self,
        player1_id: Any,
        player2_id: Any,
        first_turn: Any | None = None,
    ) -> None:
        if player1_id == player2_id:
            raise LogicError("INVALID_PARAMS", "Hai người chơi phải có ID khác nhau")

        self.player1_id = player1_id
        self.player2_id = player2_id
        self.players = (player1_id, player2_id)

        # Chọn người đi trước: nếu không chỉ định, ngẫu nhiên 50/50
        if first_turn in self.players:
            self.current_turn = first_turn
        else:
            self.current_turn = random.choice([player1_id, player2_id])

        self.first_turn = self.current_turn

        self.boards: dict[Any, Board] = {
            player1_id: Board(),
            player2_id: Board(),
        }
        self.ready_players: set[Any] = set()

        self.state = self.STATE_PLACING
        self.winner: Any | None = None
        self.loser: Any | None = None
        self.end_reason: str | None = None  # ALL_SHIPS_SUNK | SURRENDER | DISCONNECT

        self.total_shots: int = 0
        self.player_shots: dict[Any, int] = {player1_id: 0, player2_id: 0}

        self.created_at = datetime.now(timezone.utc)
        self.started_at: datetime | None = None
        self.ended_at: datetime | None = None

        self._lock = threading.Lock()

    def _validate_player(self, player_id: Any) -> None:
        if player_id not in self.players:
            raise LogicError("INVALID_PARAMS", f"Người chơi {player_id} không thuộc ván đấu này")

    def get_opponent(self, player_id: Any) -> Any:
        self._validate_player(player_id)
        return self.player2_id if player_id == self.player1_id else self.player1_id

    def get_board(self, player_id: Any) -> Board:
        self._validate_player(player_id)
        return self.boards[player_id]

    def place_ships(self, player_id: Any, ships: Any) -> None:
        """
        Đặt hoặc cập nhật vị trí tàu của người chơi trước khi bấm READY.
        """
        self._validate_player(player_id)
        with self._lock:
            if self.state != self.STATE_PLACING:
                raise BadStateError("Chỉ có thể đặt tàu ở trạng thái PLACING")

            if player_id in self.ready_players:
                raise BadStateError("Đã bấm sẵn sàng, không thể đổi lại vị trí tàu")

            self.boards[player_id].place_ships(ships)

    def ready(self, player_id: Any) -> bool:
        """
        Đánh dấu người chơi đã sẵn sàng.
        Trả về True nếu cả hai đều đã READY và ván bắt đầu (chuyển sang PLAYING).
        Trả về False nếu đang chờ người chơi còn lại.
        """
        self._validate_player(player_id)
        with self._lock:
            if self.state != self.STATE_PLACING:
                raise BadStateError("Chỉ có thể bấm READY ở trạng thái PLACING")

            if not self.boards[player_id].is_placed:
                raise NotPlacedError("Chưa đặt tàu hợp lệ trước khi sẵn sàng")

            self.ready_players.add(player_id)

            if len(self.ready_players) == 2:
                self.state = self.STATE_PLAYING
                self.started_at = datetime.now(timezone.utc)
                return True
            return False

    def fire(self, player_id: Any, x: int, y: int) -> FireResult:
        """
        Thực hiện một phát bắn vào tọa độ (x, y) trên bàn cờ đối thủ.
        Luật chơi:
        - HIT_KEEPS_TURN = True: Bắn trúng (HIT hoặc SUNK) thì bắn tiếp.
        - Bắn trượt (MISS) thì đổi lượt sang đối thủ.
        - Bắn ô ngoài bảng hoặc ô đã bắn: ném lỗi, KHÔNG mất lượt.
        """
        self._validate_player(player_id)
        with self._lock:
            if self.state != self.STATE_PLAYING:
                raise BadStateError("Ván đấu chưa bắt đầu hoặc đã kết thúc")

            if player_id != self.current_turn:
                raise NotYourTurnError("Chưa đến lượt của bạn")

            opponent_id = self.get_opponent(player_id)
            target_board = self.boards[opponent_id]

            # Bắn vào bàn cờ đối thủ (sẽ ném OutOfBoundsError hoặc AlreadyFiredError nếu vi phạm)
            shot = target_board.fire(x, y)

            self.total_shots += 1
            self.player_shots[player_id] += 1

            if target_board.all_sunk:
                # Toàn bộ tàu đối thủ đã chìm -> Người bắn chiến thắng
                self.state = self.STATE_FINISHED
                self.ended_at = datetime.now(timezone.utc)
                self.winner = player_id
                self.loser = opponent_id
                self.end_reason = "ALL_SHIPS_SUNK"
                game_over = True
                next_turn = None
            else:
                game_over = False
                if shot.result in ("HIT", "SUNK"):
                    # Bắn trúng -> được bắn tiếp
                    self.current_turn = player_id
                else:
                    # Bắn trượt -> đổi lượt
                    self.current_turn = opponent_id
                next_turn = self.current_turn

            return FireResult(
                x=shot.x,
                y=shot.y,
                result=shot.result,
                sunk_ship=shot.sunk_ship,
                sunk_cells=shot.sunk_cells,
                next_turn=next_turn,
                game_over=game_over,
                shooter=player_id,
                target=opponent_id,
                winner=self.winner,
                loser=self.loser,
                end_reason=self.end_reason,
                total_shots=self.total_shots,
            )

    def surrender(self, player_id: Any) -> dict[str, Any]:
        """
        Xử lý khi một người chơi đầu hàng / rời trận.
        - Ở PLACING: Hủy trận (CANCELLED), đối thủ nhận MATCH_CANCELLED. Không tính thắng thua.
        - Ở PLAYING: Người đầu hàng thua, đối thủ thắng (WIN, reason: 'SURRENDER').
        """
        self._validate_player(player_id)
        with self._lock:
            opponent_id = self.get_opponent(player_id)

            if self.state == self.STATE_PLACING:
                self.state = self.STATE_CANCELLED
                self.end_reason = "SURRENDER"
                self.ended_at = datetime.now(timezone.utc)
                return {
                    "action": "CANCELLED",
                    "reason": "OPPONENT_LEFT",
                    "cancelled_by": player_id,
                    "opponent_id": opponent_id,
                }

            if self.state == self.STATE_PLAYING:
                self.state = self.STATE_FINISHED
                self.ended_at = datetime.now(timezone.utc)
                self.winner = opponent_id
                self.loser = player_id
                self.end_reason = "SURRENDER"
                return {
                    "action": "GAME_OVER",
                    "game_over": True,
                    "winner": opponent_id,
                    "loser": player_id,
                    "reason": "SURRENDER",
                    "total_shots": self.total_shots,
                }

            raise BadStateError("Không thể đầu hàng ở trạng thái này")

    def disconnect(self, player_id: Any) -> dict[str, Any]:
        """
        Xử lý khi một người chơi mất kết nối mạng.
        - Ở PLACING: Đối thủ nhận MATCH_CANCELLED.
        - Ở PLAYING: Đối thủ thắng với reason 'DISCONNECT'.
        """
        self._validate_player(player_id)
        with self._lock:
            opponent_id = self.get_opponent(player_id)

            if self.state == self.STATE_PLACING:
                self.state = self.STATE_CANCELLED
                self.end_reason = "DISCONNECT"
                self.ended_at = datetime.now(timezone.utc)
                return {
                    "action": "CANCELLED",
                    "reason": "OPPONENT_LEFT",
                    "cancelled_by": player_id,
                    "opponent_id": opponent_id,
                }

            if self.state == self.STATE_PLAYING:
                self.state = self.STATE_FINISHED
                self.ended_at = datetime.now(timezone.utc)
                self.winner = opponent_id
                self.loser = player_id
                self.end_reason = "DISCONNECT"
                return {
                    "action": "GAME_OVER",
                    "game_over": True,
                    "winner": opponent_id,
                    "loser": player_id,
                    "reason": "DISCONNECT",
                    "total_shots": self.total_shots,
                }

            return {"action": "NONE"}

    @property
    def is_game_over(self) -> bool:
        return self.state in (self.STATE_FINISHED, self.STATE_CANCELLED)
