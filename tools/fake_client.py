"""Simple interactive TCP client for manual protocol testing."""
import json
import socket
import sys
from threading import Thread


def main():
    host = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 5000
    with socket.create_connection((host, port), timeout=5) as conn:
        conn.settimeout(None)

        def receiver():
            with conn.makefile("rb") as reader:
                for line in reader:
                    print("\n<", line.decode("utf-8").rstrip(), "\n> ", end="", flush=True)

        Thread(target=receiver, daemon=True).start()
        print('Type a JSON object, e.g. {"type":"PING","request_id":"1"}')
        print('Or: {"type":"JOIN_QUEUE","payload":{"display_name":"Alice"}}')
        try:
            while True:
                raw = input("> ")
                if raw.lower() in {"exit", "quit"}:
                    break
                try:
                    data = json.loads(raw)
                    if not isinstance(data, dict):
                        raise ValueError("Message must be JSON object")
                except (json.JSONDecodeError, ValueError) as exc:
                    print("Invalid input:", exc)
                    continue
                conn.sendall((json.dumps(data, ensure_ascii=False) + "\n").encode("utf-8"))
        except (EOFError, KeyboardInterrupt, OSError):
            pass


if __name__ == "__main__":
    main()
