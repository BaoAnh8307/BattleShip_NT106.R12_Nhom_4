"""Interactive demo client for BattleShip PROTOCOL v1.

Start the real integrated server (with SQL Server available) first:
    python -m server.main
Open TWO terminals, one for each player:
    python fake_client.py

Commands: register NAME PASS, login NAME PASS, find, place, ready,
          fire X Y, surrender, stats, history [LIMIT], ping, help, quit.
You can also paste full JSON (type/seq/data); client seq is added if absent.
"""
from __future__ import annotations

import argparse
import json
import socket
import threading

FLEET = [
    {"name": "CARRIER", "x": 0, "y": 0, "dir": "H"},
    {"name": "BATTLESHIP", "x": 0, "y": 2, "dir": "H"},
    {"name": "CRUISER", "x": 0, "y": 4, "dir": "H"},
    {"name": "SUBMARINE", "x": 0, "y": 6, "dir": "H"},
    {"name": "DESTROYER", "x": 0, "y": 8, "dir": "H"},
]

HELP = """
Commands:
  register USER PASS  Register a new account (version=1)
  login USER PASS     Log in (version=1)
  find                FIND_MATCH (search for opponent)
  place               PLACE_SHIPS using a valid sample fleet
  ready               READY (both players must be ready)
  fire X Y            FIRE at coordinate x,y (0..9)
  surrender           Leave / surrender the current match
  stats               GET_STATS (after a game)
  history [LIMIT]     GET_HISTORY (default 20)
  ping                PING -> PONG
  help                Show this help
  quit                Disconnect

Advanced: paste JSON as-is, e.g. {"type":"PING","data":{}}
The client fills in seq automatically; server responds with its own seq
and ref referencing your request's seq.

For a two-player demo: open two terminals, register/login distinct usernames
in each, type 'find' in both, then 'place' and 'ready' in both.
Check MATCH_FOUND.you_first to see who fires first.
The server does NOT send WELCOME when you connect.
"""


class DemoClient:
    def __init__(self, conn: socket.socket):
        self.conn = conn
        self._seq = 0
        self._send_lock = threading.Lock()
        self._closed = threading.Event()

    def send(self, kind: str, data: dict) -> None:
        self.send_json({"type": kind, "data": data})

    def send_json(self, message: dict) -> None:
        if not isinstance(message, dict):
            raise ValueError("Input must be a JSON object")
        if not isinstance(message.get("type"), str):
            raise ValueError("Missing message type")
        if not isinstance(message.get("data", {}), dict):
            raise ValueError("data must be a JSON object")
        with self._send_lock:
            if "seq" not in message:
                message = {**message, "seq": self._seq + 1}
            if type(message["seq"]) is not int or message["seq"] < 1:
                raise ValueError("seq must be a positive integer")
            if message["seq"] <= self._seq:
                raise ValueError("seq must be greater than the previous client seq")
            if "data" not in message:
                message = {**message, "data": {}}
            payload = (json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
            self.conn.sendall(payload)
            self._seq = message["seq"]
        print(f"  >> {message['type']} (seq={message['seq']})")

    def receive_forever(self) -> None:
        try:
            with self.conn.makefile("rb") as reader:
                for line in reader:
                    try:
                        message = json.loads(line.decode("utf-8"))
                        print("\n  << " + json.dumps(message, ensure_ascii=False, indent=2) + "\n> ", end="", flush=True)
                    except (UnicodeDecodeError, json.JSONDecodeError):
                        print("\n  << Invalid server message:", repr(line), "\n> ", end="", flush=True)
        except OSError as exc:
            if not self._closed.is_set():
                print(f"\nConnection error: {exc}")
        finally:
            self._closed.set()
            print("\nServer connection closed.")

    def close(self) -> None:
        self._closed.set()
        try:
            self.conn.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self.conn.close()


def parse_command(text: str) -> tuple[str, dict] | dict | None:
    if text.lstrip().startswith("{"):
        value = json.loads(text)
        if not isinstance(value, dict):
            raise ValueError("JSON must be an object")
        return value
    tokens = text.strip().split()
    if not tokens:
        return None
    command = tokens[0].lower()
    args = tokens[1:]
    if command in ("register", "login"):
        if len(args) != 2:
            raise ValueError(f"Usage: {command} USERNAME PASSWORD")
        return command.upper(), {"username": args[0], "password": args[1], "version": 1}
    if command == "find" and not args:
        return "FIND_MATCH", {}
    if command == "place" and not args:
        return "PLACE_SHIPS", {"ships": [dict(ship) for ship in FLEET]}
    if command == "ready" and not args:
        return "READY", {}
    if command == "fire":
        if len(args) != 2:
            raise ValueError("Usage: fire X Y")
        try:
            return "FIRE", {"x": int(args[0]), "y": int(args[1])}
        except ValueError as exc:
            raise ValueError("X and Y must be integers") from exc
    if command == "surrender" and not args:
        return "SURRENDER", {}
    if command == "stats" and not args:
        return "GET_STATS", {}
    if command == "history":
        if len(args) > 1:
            raise ValueError("Usage: history [LIMIT]")
        try:
            limit = int(args[0]) if args else 20
        except ValueError as exc:
            raise ValueError("LIMIT must be an integer") from exc
        return "GET_HISTORY", {"limit": limit}
    if command == "ping" and not args:
        return "PING", {}
    raise ValueError("Unknown command. Type 'help' for instructions.")


def main() -> None:
    parser = argparse.ArgumentParser(description="BattleShip PROTOCOL v1 demo client")
    parser.add_argument("host", nargs="?", default="127.0.0.1")
    parser.add_argument("port", nargs="?", type=int, default=5000)
    args = parser.parse_args()
    try:
        conn = socket.create_connection((args.host, args.port), timeout=5)
    except OSError as exc:
        print(f"Cannot connect to {args.host}:{args.port}: {exc}")
        return
    conn.settimeout(None)
    demo = DemoClient(conn)
    receiver = threading.Thread(target=demo.receive_forever, daemon=True)
    receiver.start()
    print(f"Connected to {args.host}:{args.port}. No initial WELCOME message (PROTOCOL v1).")
    print(HELP)
    try:
        while not demo._closed.is_set():
            try:
                raw = input("> ").strip()
            except (EOFError, KeyboardInterrupt):
                break
            if raw.lower() in {"quit", "exit"}:
                break
            if raw.lower() == "help":
                print(HELP)
                continue
            try:
                parsed = parse_command(raw)
                if parsed is None:
                    continue
                if isinstance(parsed, dict):
                    demo.send_json(parsed)
                else:
                    kind, data = parsed
                    demo.send(kind, data)
            except (ValueError, json.JSONDecodeError) as exc:
                print("Invalid command:", exc)
            except OSError as exc:
                print("Connection lost:", exc)
                break
    finally:
        demo.close()
        receiver.join(timeout=1)


if __name__ == "__main__":
    main()
