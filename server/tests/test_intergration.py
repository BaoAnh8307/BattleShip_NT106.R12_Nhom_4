"""TCP demo integration tests for the integrated BattleShip server.

The original filename `test_intergration.py` is preserved for drop-in replacement.
These tests use an in-memory fake Database; SQL Server and pyodbc are NOT required.
Run from project root:  python -m pytest -q tests/test_intergration.py
"""
from __future__ import annotations

import json
import socket
import threading
from collections.abc import Iterator

import pytest

from server.config import Config
from server.main import BattleShipTCPServer


FLEET = [
    {"name": "CARRIER", "x": 0, "y": 0, "dir": "H"},
    {"name": "BATTLESHIP", "x": 0, "y": 2, "dir": "H"},
    {"name": "CRUISER", "x": 0, "y": 4, "dir": "H"},
    {"name": "SUBMARINE", "x": 0, "y": 6, "dir": "H"},
    {"name": "DESTROYER", "x": 0, "y": 8, "dir": "H"},
]
SHIP_SIZES = {"CARRIER": 5, "BATTLESHIP": 4, "CRUISER": 3, "SUBMARINE": 3, "DESTROYER": 2}


class FakeDB:
    """Small replacement for database.database; deliberately keeps user data in RAM."""

    class DbError(Exception):
        def __init__(self, code: str):
            super().__init__(code)
            self.code = code

    def __init__(self):
        self.users: dict[str, tuple[int, str]] = {}
        self.matches: dict[int, dict] = {}

    def register(self, username: str, password: str) -> int:
        if username.lower() in (u.lower() for u in self.users):
            raise self.DbError("USERNAME_TAKEN")
        user_id = len(self.users) + 1
        self.users[username] = (user_id, password)
        return user_id

    def login(self, username: str, password: str) -> dict:
        user = next((name for name in self.users if name.lower() == username.lower()), None)
        if user is None or self.users[user][1] != password:
            raise self.DbError("AUTH_FAILED")
        return {"user_id": self.users[user][0], "username": user}

    def create_match(self, player1_id: int, player2_id: int) -> int:
        match_id = len(self.matches) + 1
        self.matches[match_id] = {
            "p1": player1_id, "p2": player2_id, "winner": None,
            "reason": None, "shots": 0, "ended": False,
        }
        return match_id

    def finish_match(self, match_id: int, winner_id: int, reason: str, total_shots: int) -> None:
        record = self.matches[match_id]
        if not record["ended"]:
            record.update(winner=winner_id, reason=reason, shots=total_shots, ended=True)

    def get_stats(self, user_id: int) -> dict:
        rows = [m for m in self.matches.values()
                if m["ended"] and user_id in (m["p1"], m["p2"])]
        wins = sum(m["winner"] == user_id for m in rows)
        return {"played": len(rows), "wins": wins, "losses": len(rows) - wins}

    def get_history(self, user_id: int, limit: int = 20) -> list[dict]:
        result = []
        for mid, m in reversed(list(self.matches.items())):
            if m["ended"] and user_id in (m["p1"], m["p2"]):
                other_id = m["p2"] if m["p1"] == user_id else m["p1"]
                other = next(name for name, value in self.users.items() if value[0] == other_id)
                result.append({
                    "match_id": mid, "opponent": other,
                    "result": "WIN" if m["winner"] == user_id else "LOSE",
                    "total_shots": m["shots"],
                    "started_at": "2026-01-01T00:00:00Z",
                    "ended_at": "2026-01-01T00:05:00Z",
                })
        return result[:limit]


class TestClient:
    """One TCP socket with separate monotonic client and server sequence counters."""
    __test__ = False  # pytest must not mistake this for a test class.

    def __init__(self, server: BattleShipTCPServer):
        self.sock = socket.create_connection(server.server_address, timeout=2)
        self.sock.settimeout(3)
        self.reader = self.sock.makefile("rb")
        self.last_client_seq = 0
        self.last_server_seq = 0

    def send(self, kind: str, data: dict | None = None) -> int:
        self.last_client_seq += 1
        frame = {"type": kind, "seq": self.last_client_seq, "data": {} if data is None else data}
        self.send_raw((json.dumps(frame, ensure_ascii=False) + "\n").encode("utf-8"))
        return self.last_client_seq

    def send_raw(self, wire: bytes) -> None:
        self.sock.sendall(wire)

    def receive(self, kind: str | None = None) -> dict:
        line = self.reader.readline()
        assert line, "Server disconnected before sending the expected response"
        message = json.loads(line.decode("utf-8"))
        assert message["seq"] == self.last_server_seq + 1, message
        assert isinstance(message["data"], dict), message
        self.last_server_seq = message["seq"]
        if kind is not None:
            assert message["type"] == kind, message
        return message

    def request(self, kind: str, data: dict | None, response_kind: str) -> dict:
        seq = self.send(kind, data)
        message = self.receive(response_kind)
        assert message.get("ref") == seq, message
        return message

    def close(self) -> None:
        try:
            self.sock.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self.reader.close()
        self.sock.close()


@pytest.fixture
def running_server() -> Iterator[tuple[BattleShipTCPServer, FakeDB, list[TestClient]]]:
    db = FakeDB()
    server = BattleShipTCPServer(Config(host="127.0.0.1", port=0, idle_timeout=10), db)
    thread = threading.Thread(target=server.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True)
    thread.start()
    clients: list[TestClient] = []
    try:
        yield server, db, clients
    finally:
        for client in clients:
            client.close()
        server.shutdown()
        server.server_close()
        thread.join(timeout=3)


def connect(running_server) -> TestClient:
    server, _, clients = running_server
    client = TestClient(server)
    clients.append(client)
    # Protocol v1 does NOT send WELCOME on connect.
    return client


def logged_in(running_server, username: str) -> TestClient:
    client = connect(running_server)
    account = {"username": username, "password": "demopass123", "version": 1}
    client.request("REGISTER", account, "ACK")
    login_ok = client.request("LOGIN", account, "LOGIN_OK")
    assert login_ok["data"]["username"] == username
    return client


def matched(running_server):
    alice = logged_in(running_server, "Alice_01")
    bob = logged_in(running_server, "Bob_02")
    alice.request("FIND_MATCH", {}, "ACK")
    bob.request("FIND_MATCH", {}, "ACK")
    m1 = alice.receive("MATCH_FOUND")
    m2 = bob.receive("MATCH_FOUND")
    assert "ref" not in m1 and "ref" not in m2
    assert m1["data"]["opponent"] == "Bob_02"
    assert m2["data"]["opponent"] == "Alice_01"
    assert m1["data"]["you_first"] != m2["data"]["you_first"]
    return alice, bob, m1["data"]["you_first"]


def started(running_server):
    a, b, first_a = matched(running_server)
    a.request("PLACE_SHIPS", {"ships": FLEET}, "ACK")
    b.request("PLACE_SHIPS", {"ships": FLEET}, "ACK")
    a.request("READY", {}, "ACK")
    b.request("READY", {}, "ACK")
    a.receive("GAME_START")
    b.receive("GAME_START")
    return a, b, first_a


def test_ping_split_across_tcp_packets(running_server):
    client = connect(running_server)
    client.send_raw(b'{"type":"PI')
    client.send_raw(b'NG","seq":1,"data":{}}\n')
    response = client.receive("PONG")
    assert response["ref"] == 1
    assert response["data"] == {}
    client.last_client_seq = 1  # The raw request consumed client seq 1.
    assert client.request("PING", {}, "PONG")["data"] == {}


def test_matchmaking_and_disconnect_before_game_start(running_server):
    _, db, _ = running_server
    alice, bob, _ = matched(running_server)
    bob.close()
    cancelled = alice.receive("MATCH_CANCELLED")
    assert cancelled["data"]["reason"] == "OPPONENT_LEFT"
    assert db.matches == {}  # GAME_START never happened, so no DB row.
    assert alice.request("GET_STATS", {}, "STATS")["data"]["played"] == 0


def test_complete_game_saves_result_and_history(running_server):
    _, db, _ = running_server
    a, b, first_a = started(running_server)
    shooter, target = (a, b) if first_a else (b, a)
    assert len(db.matches) == 1
    assert shooter.request("FIRE", {"x": -1, "y": 0}, "ERROR")["data"]["code"] == "OUT_OF_BOUNDS"
    assert target.request("FIRE", {"x": 0, "y": 0}, "ERROR")["data"]["code"] == "NOT_YOUR_TURN"
    positions = [(ship["x"] + d, ship["y"]) for ship in FLEET
                 for d in range(SHIP_SIZES[ship["name"]])]
    for number, (x, y) in enumerate(positions, start=1):
        own = shooter.request("FIRE", {"x": x, "y": y}, "FIRE_RESULT")
        other = target.receive("OPPONENT_FIRED")
        assert "ref" not in other
        assert own["data"]["result"] in ("HIT", "SUNK")
        assert own["data"]["game_over"] is (number == 17)
        assert own["data"]["next_turn"] == (None if number == 17 else "you")
        assert other["data"]["next_turn"] == (None if number == 17 else "opponent")
        if number == 1:
            assert shooter.request("FIRE", {"x": x, "y": y}, "ERROR")["data"]["code"] == "ALREADY_FIRED"
    assert shooter.receive("GAME_OVER")["data"] == {
        "result": "WIN", "reason": "ALL_SHIPS_SUNK", "total_shots": 17,
    }
    assert target.receive("GAME_OVER")["data"]["result"] == "LOSE"
    assert db.matches[1]["ended"] is True
    assert db.matches[1]["shots"] == 17
    assert shooter.request("GET_STATS", {}, "STATS")["data"] == {
        "played": 1, "wins": 1, "losses": 0,
    }
    assert target.request("GET_STATS", {}, "STATS")["data"]["losses"] == 1
    history = shooter.request("GET_HISTORY", {"limit": 5}, "HISTORY")
    assert history["data"]["matches"][0]["result"] == "WIN"


def test_fire_before_login_is_bad_state_not_not_implemented(running_server):
    client = connect(running_server)
    err = client.request("FIRE", {"x": 1, "y": 2}, "ERROR")
    assert err["data"]["code"] == "BAD_STATE"


def test_bad_json_does_not_crash_server(running_server):
    client = connect(running_server)
    client.send_raw(b'{broken}\n')
    error = client.receive("ERROR")
    assert error["data"]["code"] == "BAD_JSON"
    assert "ref" not in error
    assert client.request("PING", {}, "PONG")["data"] == {}


def test_line_too_long_closes_connection(running_server):
    client = connect(running_server)
    client.send_raw(b"x" * (64 * 1024 + 1))
    error = client.receive("ERROR")
    assert error["data"]["code"] == "LINE_TOO_LONG"
    assert client.reader.readline() == b""  # Explicit disconnect per PROTOCOL.md.


def test_double_find_match_rejected(running_server):
    client = logged_in(running_server, "Waiting_1")
    client.request("FIND_MATCH", {}, "ACK")
    error = client.request("FIND_MATCH", {}, "ERROR")
    assert error["data"]["code"] == "BAD_STATE"


def test_ready_without_ships_and_surrender_cancels(running_server):
    _, db, _ = running_server
    a, b, _ = matched(running_server)
    assert a.request("READY", {}, "ERROR")["data"]["code"] == "NOT_PLACED"
    a.request("PLACE_SHIPS", {"ships": FLEET}, "ACK")
    a.request("READY", {}, "ACK")
    a.request("SURRENDER", {}, "ACK")
    assert b.receive("MATCH_CANCELLED")["data"]["reason"] == "OPPONENT_LEFT"
    assert db.matches == {}


def test_surrender_after_game_start_persists_loss(running_server):
    _, db, _ = running_server
    a, b, _ = started(running_server)
    seq = a.send("SURRENDER")
    over_a = a.receive("GAME_OVER")
    over_b = b.receive("GAME_OVER")
    assert "ref" not in over_a  # GAME_OVER is a broadcast, not ACK.
    assert seq > 0
    assert over_a["data"] == {"result": "LOSE", "reason": "SURRENDER", "total_shots": 0}
    assert over_b["data"]["result"] == "WIN"
    assert db.matches[1]["winner"] == 2


def test_duplicate_login_is_rejected(running_server):
    client = logged_in(running_server, "User_One")
    second = connect(running_server)
    result = second.request("LOGIN", {
        "username": "User_One", "password": "demopass123", "version": 1,
    }, "ERROR")
    assert result["data"]["code"] == "ALREADY_LOGGED_IN"
    assert client.request("PING", {}, "PONG")["data"] == {}
