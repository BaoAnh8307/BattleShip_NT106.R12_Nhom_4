
from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

from .protocol import ProtocolError, event
from .session_manager import ClientSession, Notification, SessionManager

GAME_MESSAGES = {"REGISTER", "LOGIN", "PLACE_SHIPS", "READY", "FIRE"}


def dispatch(client: ClientSession, message: dict[str, Any], manager: SessionManager) -> list[Notification]:
    type_ = message["type"]
    request_id = message.get("request_id")
    payload = message.get("payload", {})
    if type_ == "PING":
        return [(client, event("PONG", {"utc": datetime.now(timezone.utc).isoformat()}, request_id))]
    if type_ == "JOIN_QUEUE":
        name = payload.get("display_name", "")
        notes = manager.join_queue(client, name)
        for target, response in notes:
            if target is client and response["type"] == "QUEUE_JOINED" and request_id is not None:
                response["request_id"] = request_id
        return notes
    if type_ == "LEAVE_QUEUE":
        notes = manager.leave_queue(client)
        if request_id is not None:
            notes[0][1]["request_id"] = request_id
        return notes
    if type_ == "GET_STATUS":
        return [(client, event("STATUS", manager.status(client), request_id))]
    if type_ in GAME_MESSAGES:
        raise ProtocolError("NOT_IMPLEMENTED", f"{type_} awaits the Game Logic / Database integration")
    raise ProtocolError("UNKNOWN_TYPE", f"Unsupported type: {type_[:50]}")

