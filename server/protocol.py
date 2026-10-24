
from __future__ import annotations

import json
from typing import Any


class ProtocolError(Exception):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code
        self.message = message


def decode_message(line: bytes) -> dict[str, Any]:
    try:
        obj = json.loads(line.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProtocolError("BAD_JSON", "Invalid UTF-8 JSON") from exc
    if not isinstance(obj, dict):
        raise ProtocolError("BAD_MESSAGE", "JSON message must be an object")
    if not isinstance(obj.get("type"), str) or not obj["type"]:
        raise ProtocolError("BAD_MESSAGE", "'type' must be a non-empty string")
    if "request_id" in obj and (not isinstance(obj["request_id"], str) or len(obj["request_id"]) > 64):
        raise ProtocolError("BAD_MESSAGE", "'request_id' must be a string <= 64 characters")
    if "payload" in obj and not isinstance(obj["payload"], dict):
        raise ProtocolError("BAD_MESSAGE", "'payload' must be an object")
    return obj


def encode_message(obj: dict[str, Any]) -> bytes:
    return (json.dumps(obj, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")


def event(type_: str, payload: dict[str, Any] | None = None, request_id: str | None = None) -> dict[str, Any]:
    obj: dict[str, Any] = {"type": type_, "payload": payload or {}}
    if request_id is not None:
        obj["request_id"] = request_id
    return obj


def error(code: str, message: str, request_id: str | None = None) -> dict[str, Any]:
    return event("ERROR", {"code": code, "message": message}, request_id)




