"""Tests for BattleShip PROTOCOL.md v1 (type/seq/ref/data).

Run from the BattleShip_Integrated project root:
    python -m pytest -q tests/test_protocol.py
"""
import pytest

from server.protocol import (
    ProtocolError,
    decode_message,
    encode_message,
    error,
    event,
)


def test_json_lines_roundtrip_unicode():
    original = {
        "type": "REGISTER",
        "seq": 1,
        "data": {"username": "Kien_01", "password": "matkhau123", "version": 1, "note": "Kiên"},
    }
    wire = encode_message(original)
    assert isinstance(wire, bytes)
    assert wire.endswith(b"\n")
    assert wire.count(b"\n") == 1
    assert decode_message(wire) == original


def test_ping_envelope_and_response_helpers():
    assert decode_message(b'{"type":"PING","seq":2,"data":{}}\n') == {
        "type": "PING", "seq": 2, "data": {},
    }
    # event/error build a message template; ClientSession.send adds server-side seq.
    assert event("PONG", ref=2) == {"type": "PONG", "ref": 2, "data": {}}
    assert error("BAD_STATE", "Sai trang thai", ref=3) == {
        "type": "ERROR", "ref": 3,
        "data": {"code": "BAD_STATE", "message": "Sai trang thai"},
    }


@pytest.mark.parametrize(
    "wire,expected_code",
    [
        (b"not-json\n", "BAD_JSON"),
        (b"\xff\n", "BAD_JSON"),
        (b"[]\n", "INVALID_PARAMS"),
        (b'{"type":1,"seq":1,"data":{}}\n', "INVALID_PARAMS"),
        (b'{"type":"ping","seq":1,"data":{}}\n', "INVALID_PARAMS"),
        (b'{"type":"PING","data":{}}\n', "INVALID_PARAMS"),
        (b'{"type":"PING","seq":0,"data":{}}\n', "INVALID_PARAMS"),
        (b'{"type":"PING","seq":true,"data":{}}\n', "INVALID_PARAMS"),
        (b'{"type":"PING","seq":1,"data":3}\n', "INVALID_PARAMS"),
        (b'{"type":"PING","seq":1,"data":[]}\n', "INVALID_PARAMS"),
        (b'{"type":"PING","seq":1,"data":{},"ref":2}\n', "INVALID_PARAMS"),
        (b'{"type":"PING","seq":1,"payload":{}}\n', "INVALID_PARAMS"),
    ],
)
def test_reject_invalid_messages(wire, expected_code):
    with pytest.raises(ProtocolError) as exc:
        decode_message(wire)
    assert exc.value.code == expected_code


def test_error_preserves_request_reference_when_seq_was_valid():
    # Type is correct but data is invalid -> can still correlate to client seq=17.
    with pytest.raises(ProtocolError) as exc:
        decode_message(b'{"type":"PING","seq":17,"data":[]}\n')
    assert exc.value.code == "INVALID_PARAMS"
    assert exc.value.ref == 17

    # When JSON itself is broken, seq cannot be recovered.
    with pytest.raises(ProtocolError) as exc:
        decode_message(b'{broken}\n')
    assert exc.value.code == "BAD_JSON"
    assert exc.value.ref is None
