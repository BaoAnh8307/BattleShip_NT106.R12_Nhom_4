from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from threading import Lock
from typing import Any
from uuid import uuid4
import socket

from .protocol import ProtocolError, event


@dataclass(eq=False)
class ClientSession:
    conn: socket.socket
    session_id: str = field(default_factory=lambda: uuid4().hex)
    display_name: str | None = None
    state: str = "CONNECTED"
    match_id: str | None = None
    peer_session_id: str | None = None
    send_lock: Lock = field(default_factory=Lock, repr=False)

    def send(self, msg: dict[str, Any]) -> None:
        from .protocol import encode_message
        data = encode_message(msg)
        with self.send_lock:
            self.conn.sendall(data)


Notification = tuple[ClientSession, dict[str, Any]]


class SessionManager:
    def __init__(self):
        self._lock = Lock()
        self._sessions: dict[str, ClientSession] = {}
        self._waiting: deque[str] = deque()

    def register(self, client: ClientSession) -> None:
        with self._lock:
            self._sessions[client.session_id] = client

    def unregister(self, client: ClientSession) -> list[Notification]:
        notes: list[Notification] = []
        with self._lock:
            if self._sessions.pop(client.session_id, None) is None:
                return notes
            self._waiting = deque(sid for sid in self._waiting if sid != client.session_id)
            if client.peer_session_id:
                peer = self._sessions.get(client.peer_session_id)
                if peer is not None and peer.match_id == client.match_id:
                    peer.state = "CONNECTED"
                    peer.match_id = None
                    peer.peer_session_id = None
                    notes.append((peer, event("MATCH_ENDED", {"reason": "OPPONENT_DISCONNECTED"})))
            client.state = "DISCONNECTED"
            client.match_id = None
            client.peer_session_id = None
        return notes

    def join_queue(self, client: ClientSession, name: str) -> list[Notification]:
        name = name.strip() if isinstance(name, str) else ""
        if not (1 <= len(name) <= 24) or any(ord(ch) < 32 for ch in name):
            raise ProtocolError("INVALID_NAME", "display_name must have 1..24 printable characters")

        notes: list[Notification] = []
        with self._lock:
            if client.session_id not in self._sessions:
                raise ProtocolError("NOT_CONNECTED", "Session has disconnected")
            if client.state != "CONNECTED":
                raise ProtocolError("BAD_STATE", "Client is already queued or matched")
            client.display_name = name
            client.state = "QUEUED"
            self._waiting.append(client.session_id)
            notes.append((client, event("QUEUE_JOINED", {"status": "waiting"})))
            if len(self._waiting) >= 2:
                first_id, second_id = self._waiting.popleft(), self._waiting.popleft()
                first, second = self._sessions[first_id], self._sessions[second_id]
                match_id = uuid4().hex
                first.state = second.state = "MATCHED"
                first.match_id = second.match_id = match_id
                first.peer_session_id = second.session_id
                second.peer_session_id = first.session_id
                notes.extend([
                    (first, event("MATCH_FOUND", {"match_id": match_id, "opponent": second.display_name, "role": "P1"})),
                    (second, event("MATCH_FOUND", {"match_id": match_id, "opponent": first.display_name, "role": "P2"})),
                ])
        return notes

    def leave_queue(self, client: ClientSession) -> list[Notification]:
        with self._lock:
            if client.state != "QUEUED":
                raise ProtocolError("BAD_STATE", "Client is not in queue")
            self._waiting = deque(sid for sid in self._waiting if sid != client.session_id)
            client.state = "CONNECTED"
        return [(client, event("QUEUE_LEFT"))]

    def status(self, client: ClientSession) -> dict[str, Any]:
        with self._lock:
            return {"state": client.state, "match_id": client.match_id, "waiting_count": len(self._waiting)}



