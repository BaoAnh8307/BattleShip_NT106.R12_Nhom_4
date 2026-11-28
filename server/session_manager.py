"""Manage authenticated sockets, matchmaking, game lifecycle and DB records.

The manager uses its own lock to protect shared dictionaries / matchmaking,
while keeping slow database I/O outside the lock.
"""
from __future__ import annotations

import logging
import random
import socket
from collections import deque
from dataclasses import dataclass, field
from threading import Lock
from typing import Any
from uuid import uuid4

from logic import GameSession, LogicError
from .protocol import ProtocolError, encode_message, event

log = logging.getLogger("battleship.server.sessions")


@dataclass(eq=False)
class ClientSession:
    conn: socket.socket
    session_id: str = field(default_factory=lambda: uuid4().hex)
    state: str = "CONNECTED"
    user_id: int | None = None
    username: str | None = None
    match_id: str | None = None  # ID nội bộ, KHÔNG phải Matches.match_id trong SQL.
    room_code: str | None = None
    peer_session_id: str | None = None
    send_lock: Lock = field(default_factory=Lock, repr=False)
    next_server_seq: int = 1

    def send(self, msg: dict[str, Any]) -> None:
        # Allocate seq and send while holding same lock, so numbering is in wire order.
        with self.send_lock:
            outbound = {"type": msg["type"], "seq": self.next_server_seq}
            if "ref" in msg:
                outbound["ref"] = msg["ref"]
            outbound["data"] = msg["data"]
            self.conn.sendall(encode_message(outbound))
            self.next_server_seq += 1


Notification = tuple[ClientSession, dict[str, Any]]


@dataclass
class Match:
    game: GameSession
    first_id: str
    second_id: str
    db_match_id: int | None = None


@dataclass
class Room:
    code: str
    host: ClientSession
    guest: ClientSession | None = None
    locked: bool = False
    ready: bool = False

    def snapshot(self):
        return {"code": self.code, "host": self.host.username,
                "guest": self.guest.username if self.guest else None,
                "locked": self.locked, "ready": self.ready}


class SessionManager:
    def __init__(self, db: Any):
        self.db = db  # Module database.database hoặc fake DB dùng khi test.
        self._lock = Lock()
        self._sessions: dict[str, ClientSession] = {}
        self._online: dict[int, str] = {}  # user_id -> session_id
        self._rooms: dict[str, Room] = {}
        self._waiting: deque[str] = deque()
        self._matches: dict[str, Match] = {}

    def register(self, client: ClientSession) -> None:
        with self._lock:
            self._sessions[client.session_id] = client

    def _require_state(self, client: ClientSession, *states: str) -> None:
        if client.state not in states:
            raise ProtocolError("BAD_STATE", f"{client.state}: không được thực hiện lệnh này")

    def _get_match(self, client: ClientSession) -> Match:
        match = self._matches.get(client.match_id or "")
        if match is None or client.user_id not in match.game.players:
            raise ProtocolError("BAD_STATE", "Không tìm thấy trận đang chơi")
        return match

    def login(self, client: ClientSession, username: str, password: str) -> dict[str, Any]:
        with self._lock:
            self._require_state(client, "CONNECTED")
        account = self.db.login(username, password)
        with self._lock:
            self._require_state(client, "CONNECTED")
            user_id = int(account["user_id"])
            if user_id in self._online:
                raise ProtocolError("ALREADY_LOGGED_IN", "Tài khoản đang đăng nhập ở kết nối khác")
            client.user_id, client.username, client.state = user_id, account["username"], "AUTHED"
            self._online[user_id] = client.session_id
            return {"user_id": user_id, "username": client.username}

    def find_match(self, client: ClientSession) -> list[Notification]:
        with self._lock:
            self._require_state(client, "AUTHED")
            if client.user_id is None or client.username is None:
                raise ProtocolError("BAD_STATE", "Bạn chưa đăng nhập")
            client.state = "IN_QUEUE"
            self._waiting.append(client.session_id)
            notes: list[Notification] = []
            # Caller adds ACK *before* these notifications.
            while len(self._waiting) >= 2:
                p1_id, p2_id = self._waiting.popleft(), self._waiting.popleft()
                p1, p2 = self._sessions[p1_id], self._sessions[p2_id]
                notes.extend(self._start_pair(p1, p2))
            return notes

    def _start_pair(self, p1: ClientSession, p2: ClientSession) -> list[Notification]:
        p1_id, p2_id = p1.session_id, p2.session_id
        match_id = uuid4().hex
        first_user = random.choice((p1.user_id, p2.user_id))
        game = GameSession(p1.user_id, p2.user_id, first_turn=first_user)
        self._matches[match_id] = Match(game, p1_id, p2_id)
        p1.state = p2.state = "PLACING"
        p1.match_id = p2.match_id = match_id
        p1.peer_session_id, p2.peer_session_id = p2_id, p1_id
        return [
            (p1, event("MATCH_FOUND", {"opponent": p2.username, "you_first": p1.user_id == first_user})),
            (p2, event("MATCH_FOUND", {"opponent": p1.username, "you_first": p2.user_id == first_user})),
        ]

    def _leave_room(self, client: ClientSession) -> None:
        room = self._rooms[client.room_code]
        if client is room.host:
            del self._rooms[room.code]
            if room.guest:
                room.guest.room_code = None
                room.guest.state = "AUTHED"
        else:
            room.guest = None
            room.ready = False
        client.room_code = None
        client.state = "AUTHED"

    def room_request(self, client, kind, data, seq) -> list[Notification]:
        with self._lock:
            if kind == "LIST_ROOMS":
                rooms = [r.snapshot() for r in self._rooms.values() if not r.locked and not r.guest]
                return [(client, event("ROOM_LIST", {"rooms": rooms}, seq))]
            if kind == "CREATE_ROOM":
                code = f"BS-{random.randrange(10000):04d}"
                while code in self._rooms:
                    code = f"BS-{random.randrange(10000):04d}"
                room = Room(code, client)
                self._rooms[code] = room
            elif kind == "JOIN_ROOM":
                code = data.get("code")
                room = self._rooms.get(code) if isinstance(code, str) else None
                if not room or room.locked or room.guest:
                    raise ProtocolError("ROOM_UNAVAILABLE", "Phòng đã đóng, bị khóa hoặc đã đầy.")
                room.guest = client
            else:
                room = self._rooms.get(client.room_code)
                if not room:
                    raise ProtocolError("ROOM_CLOSED", "Phòng đã đóng.")
                if kind == "LEAVE_ROOM":
                    self._leave_room(client)
                    return [(client, event("ACK", ref=seq))]
                if kind == "START_ROOM":
                    if client is not room.host or not room.guest or not room.ready:
                        raise ProtocolError("ROOM_NOT_READY", "Chỉ chủ phòng được bắt đầu khi khách sẵn sàng.")
                    notes = self._start_pair(room.host, room.guest)
                    room.host.room_code = room.guest.room_code = None
                    del self._rooms[room.code]
                    return [(client, event("ACK", ref=seq)), *notes]
                if kind in ("LOCK_ROOM", "ROOM_READY"):
                    value = data.get("value")
                    if type(value) is not bool:
                        raise ProtocolError("INVALID_PARAMS", "value phải là boolean")
                    if kind == "LOCK_ROOM" and client is room.host:
                        room.locked = value
                    elif kind == "ROOM_READY" and client is room.guest:
                        room.ready = value
                    else:
                        raise ProtocolError("ROOM_ROLE", "Thao tác không dành cho vai trò của bạn.")
            client.room_code = room.code
            client.state = "IN_ROOM"
            return [(client, event("ROOM", room.snapshot(), seq))]

    def place_ships(self, client: ClientSession, ships: Any) -> None:
        with self._lock:
            self._require_state(client, "PLACING")
            self._get_match(client).game.place_ships(client.user_id, ships)

    def ready(self, client: ClientSession) -> list[Notification]:
        with self._lock:
            self._require_state(client, "PLACING")
            match = self._get_match(client)
            if not match.game.get_board(client.user_id).is_placed:
                raise LogicError("NOT_PLACED", "Chưa đặt đủ 5 tàu hợp lệ")
            p1 = self._sessions[match.first_id]
            p2 = self._sessions[match.second_id]
            other = p2 if client is p1 else p1
            need_db_match = other.state == "READY" and match.db_match_id is None
            p1_uid, p2_uid = p1.user_id, p2.user_id

        db_match_id: int | None = None
        if need_db_match:
            # Tạo bản ghi SQL *sau* khi hai bên đã đặt tàu và ngay trước GAME_START.
            # DB thất bại => không đổi trạng thái last player, có thể gửi READY lại.
            db_match_id = self.db.create_match(p1_uid, p2_uid)

        with self._lock:
            self._require_state(client, "PLACING")
            match = self._get_match(client)
            if db_match_id is not None and match.db_match_id is None:
                match.db_match_id = db_match_id
            p1 = self._sessions[match.first_id]
            p2 = self._sessions[match.second_id]
            started = match.game.ready(client.user_id)
            if started:
                p1.state = p2.state = "PLAYING"
                return [(p1, event("GAME_START")), (p2, event("GAME_START"))]
            client.state = "READY"
            return []

    def fire(self, client: ClientSession, x: int, y: int, request_seq: int) -> list[Notification]:
        db_payload = None
        with self._lock:
            self._require_state(client, "PLAYING")
            match = self._get_match(client)
            opponent = self._sessions[client.peer_session_id]
            outcome = match.game.fire(client.user_id, x, y)
            notes = [
                (client, event("FIRE_RESULT", outcome.for_player(client.user_id), request_seq)),
                (opponent, event("OPPONENT_FIRED", outcome.for_player(opponent.user_id))),
            ]
            if outcome.game_over:
                end_notes, db_payload = self._finish_match(client.match_id, match)
                notes.extend(end_notes)
        self._persist_match_result(db_payload)
        return notes

    def surrender(self, client: ClientSession, request_seq: int) -> list[Notification]:
        with self._lock:
            self._require_state(client, "PLACING", "READY", "PLAYING")
            match = self._get_match(client)
            opponent = self._sessions[client.peer_session_id]
            outcome = match.game.surrender(client.user_id)
            if outcome["action"] == "CANCELLED":
                self._clear_match(client.match_id, match)
                return [
                    (client, event("ACK", ref=request_seq)),
                    (opponent, event("MATCH_CANCELLED", {"reason": "OPPONENT_LEFT"})),
                ]
            notes, db_payload = self._finish_match(client.match_id, match)
        self._persist_match_result(db_payload)
        return notes

    def _clear_match(self, key: str | None, match: Match) -> None:
        self._matches.pop(key or "", None)
        for sid in (match.first_id, match.second_id):
            client = self._sessions.get(sid)
            if client is not None:
                client.state = "AUTHED" if client.user_id is not None else "CONNECTED"
                client.match_id = client.peer_session_id = None

    def _finish_match(
        self, key: str | None, match: Match
    ) -> tuple[list[Notification], tuple[int | None, int | None, str | None, int]]:
        game = match.game
        notes: list[Notification] = []
        for sid in (match.first_id, match.second_id):
            client = self._sessions.get(sid)
            if client is not None:
                notes.append((client, event("GAME_OVER", {
                    "result": "WIN" if client.user_id == game.winner else "LOSE",
                    "reason": game.end_reason,
                    "total_shots": game.total_shots,
                })))
        self._clear_match(key, match)
        return notes, (match.db_match_id, game.winner, game.end_reason, game.total_shots)

    def _persist_match_result(self, payload: tuple[int | None, int | None, str | None, int] | None) -> None:
        if payload is None:
            return
        db_match_id, winner, end_reason, total_shots = payload
        # Persist the result once; if SQL unavailable, gameplay still finishes.
        try:
            if db_match_id is None:
                raise RuntimeError("Missing SQL match_id after GAME_START")
            self.db.finish_match(db_match_id, winner, end_reason, total_shots)
        except Exception:
            log.exception("Could not persist finished match db_match_id=%s", db_match_id)

    def stats(self, client: ClientSession) -> dict[str, int]:
        with self._lock:
            self._require_state(client, "AUTHED")
            user_id = client.user_id
        return self.db.get_stats(user_id)

    def history(self, client: ClientSession, limit: int) -> list[dict[str, Any]]:
        with self._lock:
            self._require_state(client, "AUTHED")
            user_id = client.user_id
        return self.db.get_history(user_id, limit)

    def unregister(self, client: ClientSession) -> list[Notification]:
        notes: list[Notification] = []
        db_payload = None
        with self._lock:
            if self._sessions.pop(client.session_id, None) is None:
                return notes
            if client.room_code:
                self._leave_room(client)
            self._waiting = deque(sid for sid in self._waiting if sid != client.session_id)
            if client.user_id is not None and self._online.get(client.user_id) == client.session_id:
                del self._online[client.user_id]
            match = self._matches.get(client.match_id or "")
            if match is not None:
                outcome = match.game.disconnect(client.user_id)
                if outcome["action"] == "CANCELLED":
                    other = self._sessions.get(client.peer_session_id or "")
                    self._clear_match(client.match_id, match)
                    if other is not None:
                        notes.append((other, event("MATCH_CANCELLED", {"reason": "OPPONENT_LEFT"})))
                elif outcome["action"] == "GAME_OVER":
                    end_notes, db_payload = self._finish_match(client.match_id, match)
                    notes.extend((c, m) for c, m in end_notes if c is not client)
            client.state = "DISCONNECTED"
            client.match_id = client.peer_session_id = None
        self._persist_match_result(db_payload)
        return notes