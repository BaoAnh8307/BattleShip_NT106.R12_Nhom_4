import pytest
from server.protocol import ProtocolError, decode_message, encode_message


def test_json_lines_roundtrip_unicode():
    original = {"type": "PING", "payload": {"name": "Kiên"}}
    wire = encode_message(original)
    assert wire.endswith(b"\n")
    assert decode_message(wire) == original


@pytest.mark.parametrize("raw", [b"not-json\n", b"[]\n", b'{"type":1}\n', b'{"type":"PING","payload":3}\n'])
def test_reject_invalid_messages(raw):
    with pytest.raises(ProtocolError):
        decode_message(raw)
