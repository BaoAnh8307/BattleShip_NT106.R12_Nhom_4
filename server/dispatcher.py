"""Translate protocol requests into DB + game actions, produce notifications."""
from __future__ import annotations

from typing import Any

from .protocol import ProtocolError, event, require_credentials, require_version
from .session_manager import ClientSession, Notification, SessionManager

CLIENT_TYPES = {
    "REGISTER", "LOGIN", "FIND_MATCH", "PLACE_SHIPS", "READY", "FIRE", "SURRENDER",
    "GET_STATS", "GET_HISTORY", "PING",
}

ALLOWED_STATES = {
    "REGISTER": ("CONNECTED",),
    "LOGIN": ("CONNECTED",),
    "FIND_MATCH": ("AUTHED",),
    "PLACE_SHIPS": ("PLACING",),
    "READY": ("PLACING",),
    "FIRE": ("PLAYING",),
    "SURRENDER": ("PLACING", "READY", "PLAYING"),
    "GET_STATS": ("AUTHED",),
    "GET_HISTORY": ("AUTHED",),
}


def dispatch(client: ClientSession, message: dict[str, Any], manager: SessionManager) -> list[Notification]:
    kind, seq, data = message["type"], message["seq"], message["data"]
    if kind not in CLIENT_TYPES:
        raise ProtocolError("UNKNOWN_TYPE", f"Message type không hỗ trợ: {kind}")
    if kind == "PING":
        return [(client, event("PONG", ref=seq))]
    # Machine-state validation comes first, as required by PROTOCOL.md section 4.
    manager._require_state(client, *ALLOWED_STATES[kind])
    if kind == "REGISTER":
        require_version(data)
        username, password = require_credentials(data, register=True)
        manager.db.register(username, password)
        return [(client, event("ACK", ref=seq))]
    if kind == "LOGIN":
        require_version(data)
        username, password = require_credentials(data)
        result = manager.login(client, username, password)
        return [(client, event("LOGIN_OK", result, seq))]
    if kind == "FIND_MATCH":
        notes = manager.find_match(client)
        return [(client, event("ACK", ref=seq)), *notes]
    if kind == "PLACE_SHIPS":
        if "ships" not in data:
            raise ProtocolError("INVALID_PARAMS", "Thiếu data.ships")
        manager.place_ships(client, data["ships"])
        return [(client, event("ACK", ref=seq))]
    if kind == "READY":
        notes = manager.ready(client)
        return [(client, event("ACK", ref=seq)), *notes]
    if kind == "FIRE":
        if "x" not in data or "y" not in data or type(data["x"]) is not int or type(data["y"]) is not int:
            raise ProtocolError("INVALID_PARAMS", "x và y phải là số nguyên")
        return manager.fire(client, data["x"], data["y"], seq)
    if kind == "SURRENDER":
        return manager.surrender(client, seq)
    if kind == "GET_STATS":
        return [(client, event("STATS", manager.stats(client), seq))]
    if kind == "GET_HISTORY":
        limit = data.get("limit", 20)
        if type(limit) is not int or limit < 1:
            raise ProtocolError("INVALID_PARAMS", "limit phải là số nguyên dương")
        return [(client, event("HISTORY", {"matches": manager.history(client, min(limit, 50))}, seq))]
    raise AssertionError("Unreachable")
