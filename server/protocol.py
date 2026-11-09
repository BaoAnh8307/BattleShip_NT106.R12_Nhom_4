"""Wire protocol v1: UTF-8 newline-delimited JSON, type/seq/ref/data."""
from __future__ import annotations

import json
import re
from typing import Any

PROTOCOL_VERSION = 1
MAX_LINE_BYTES = 64 * 1024
USERNAME_RE = re.compile(r"[A-Za-z0-9_]{3,20}\Z")


class ProtocolError(Exception):
    def __init__(self, code: str, message: str, ref: int | None = None):
        super().__init__(message)
        self.code = code
        self.message = message
        self.ref = ref


def decode_message(line: bytes) -> dict[str, Any]:
    try:
        obj = json.loads(line.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProtocolError("BAD_JSON", "JSON hoặc UTF-8 không hợp lệ") from exc
    if not isinstance(obj, dict):
        raise ProtocolError("INVALID_PARAMS", "Message phải là JSON object")

    ref = obj.get("seq")
    ref = ref if type(ref) is int and ref >= 1 else None
    if type(obj.get("type")) is not str or not re.fullmatch(r"[A-Z][A-Z0-9_]*", obj["type"]):
        raise ProtocolError("INVALID_PARAMS", "type phải là chuỗi chữ hoa", ref)
    if ref is None:
        raise ProtocolError("INVALID_PARAMS", "seq phải là số nguyên dương", None)
    if "ref" in obj:
        raise ProtocolError("INVALID_PARAMS", "Client không được gửi trường ref", ref)
    if not isinstance(obj.get("data"), dict):
        raise ProtocolError("INVALID_PARAMS", "data phải là JSON object", ref)
    return obj


def encode_message(message: dict[str, Any]) -> bytes:
    return (json.dumps(message, ensure_ascii=False, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")


def event(type_: str, data: dict[str, Any] | None = None, ref: int | None = None) -> dict[str, Any]:
    result: dict[str, Any] = {"type": type_, "data": {} if data is None else data}
    if ref is not None:
        result["ref"] = ref
    return result


def error(code: str, message: str, ref: int | None = None) -> dict[str, Any]:
    return event("ERROR", {"code": code, "message": message}, ref)


def require_version(data: dict[str, Any]) -> None:
    version = data.get("version")
    if type(version) is not int:
        raise ProtocolError("INVALID_PARAMS", "version phải là số nguyên")
    if version != PROTOCOL_VERSION:
        raise ProtocolError("UNSUPPORTED_VERSION", "Phiên bản giao thức không hỗ trợ")


def require_credentials(data: dict[str, Any], *, register: bool = False) -> tuple[str, str]:
    username, password = data.get("username"), data.get("password")
    if type(username) is not str or type(password) is not str:
        raise ProtocolError("INVALID_PARAMS", "Thiếu username hoặc password")
    if register:
        if not USERNAME_RE.fullmatch(username):
            raise ProtocolError("INVALID_USERNAME", "Username phải gồm 3–20 ký tự A-Z, a-z, 0-9 hoặc _")
        if not 6 <= len(password) <= 64:
            raise ProtocolError("WEAK_PASSWORD", "Password phải có 6–64 ký tự")
    return username, password
