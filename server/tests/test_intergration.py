import json
import socket
import threading

import pytest

from server.config import Config
from server.main import BattleShipTCPServer


@pytest.fixture
def running_server():
    server = BattleShipTCPServer(Config(port=0, idle_timeout=3))
    thread = threading.Thread(target=server.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True)
    thread.start()
    yield server
    server.shutdown()
    server.server_close()
    thread.join(timeout=3)


def connect(server):
    sock = socket.create_connection(server.server_address, timeout=2)
    sock.settimeout(2)
    reader = sock.makefile("rb")
    assert read(reader)["type"] == "WELCOME"
    return sock, reader


def read(reader):
    return json.loads(reader.readline().decode("utf-8"))


def send(sock, obj):
    sock.sendall((json.dumps(obj) + "\n").encode("utf-8"))


def test_ping_split_across_tcp_packets(running_server):
    sock, reader = connect(running_server)
    try:
        sock.sendall(b'{"type":"PI')
        sock.sendall(b'NG","request_id":"req-1"}\n')
        result = read(reader)
        assert result["type"] == "PONG"
        assert result["request_id"] == "req-1"
    finally:
        reader.close()
        sock.close()


def test_matchmaking_and_disconnect(running_server):
    a, ar = connect(running_server)
    b, br = connect(running_server)
    try:
        send(a, {"type": "JOIN_QUEUE", "payload": {"display_name": "Alice"}})
        assert read(ar)["type"] == "QUEUE_JOINED"
        send(b, {"type": "JOIN_QUEUE", "payload": {"display_name": "Bob"}})
        assert read(br)["type"] == "QUEUE_JOINED"
        a_match = read(ar)
        b_match = read(br)
        assert a_match["type"] == b_match["type"] == "MATCH_FOUND"
        assert a_match["payload"]["match_id"] == b_match["payload"]["match_id"]
        b.shutdown(socket.SHUT_RDWR)
        b.close()
        assert read(ar)["type"] == "MATCH_ENDED"
    finally:
        ar.close()
        br.close()
        a.close()
        b.close()


def test_gameplay_not_faked(running_server):
    sock, reader = connect(running_server)
    try:
        send(sock, {"type": "FIRE", "request_id": "r2", "payload": {"x": 1, "y": 2}})
        response = read(reader)
        assert response["type"] == "ERROR"
        assert response["payload"]["code"] == "NOT_IMPLEMENTED"
        assert response["request_id"] == "r2"
    finally:
        reader.close()
        sock.close()


def test_bad_json_is_rejected_without_crashing(running_server):
    sock, reader = connect(running_server)
    try:
        sock.sendall(b'{broken}\n')
        assert read(reader)["payload"]["code"] == "BAD_JSON"
        send(sock, {"type": "PING"})
        assert read(reader)["type"] == "PONG"
    finally:
        reader.close()
        sock.close()


def test_reject_oversized_frame(running_server):
    sock, reader = connect(running_server)
    try:
        sock.sendall(b"x" * 16385)
        response = read(reader)
        assert response["type"] == "ERROR"
        assert response["payload"]["code"] == "MESSAGE_TOO_LARGE"
    finally:
        reader.close()
        sock.close()


def test_double_join_rejected(running_server):
    sock, reader = connect(running_server)
    try:
        send(sock, {"type": "JOIN_QUEUE", "request_id": "r1", "payload": {"display_name": "Alice"}})
        ack = read(reader)
        assert ack["type"] == "QUEUE_JOINED"
        assert ack["request_id"] == "r1"
        send(sock, {"type": "JOIN_QUEUE", "request_id": "r2", "payload": {"display_name": "Alice"}})
        reject = read(reader)
        assert reject["payload"]["code"] == "BAD_STATE"
        assert reject["request_id"] == "r2"
    finally:
        reader.close()
        sock.close()




