"""BattleShip TCP server. Start with `python -m server.main` from project root."""
from __future__ import annotations

import logging
import socket
import socketserver
from threading import BoundedSemaphore, Lock

from logic import LogicError
from .config import Config
from .dispatcher import dispatch
from .protocol import ProtocolError, decode_message, encode_message, error
from .session_manager import ClientSession, Notification, SessionManager

log = logging.getLogger("battleship.server")


class BattleShipHandler(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        server: BattleShipTCPServer = self.server  # type: ignore[assignment]
        conn: socket.socket = self.request
        conn.settimeout(server.config.idle_timeout)
        client = ClientSession(conn=conn)
        server.manager.register(client)
        log.info("Connected session=%s address=%s", client.session_id[:8], self.client_address)
        try:
            # No unsolicited WELCOME: it does not exist in PROTOCOL.md.
            with conn.makefile("rb") as reader:
                while True:
                    try:
                        line = reader.readline(server.config.max_line_bytes + 1)
                    except socket.timeout:
                        log.info("Idle timeout session=%s", client.session_id[:8])
                        break
                    if not line:
                        break
                    if len(line) > server.config.max_line_bytes:
                        try:
                            client.send(error("LINE_TOO_LONG", "Message vượt quá 64 KB"))
                        except OSError:
                            pass
                        break
                    if not line.endswith(b"\n"):
                        # Incomplete frame (usually peer closed connection).
                        break
                    with server.dispatch_lock:
                        try:
                            message = decode_message(line)
                            try:
                                notes = dispatch(client, message, server.manager)
                            except (ProtocolError, LogicError) as exc:
                                notes = [(client, error(exc.code, exc.message, message["seq"]))]
                            except server.db_error_class as exc:
                                notes = [(client, error(exc.code, exc.code, message["seq"]))]
                            except Exception:
                                log.exception("Unhandled request error type=%s", message["type"])
                                notes = [(client, error("SERVER_ERROR", "Lỗi nội bộ máy chủ", message["seq"]))]
                        except ProtocolError as exc:
                            notes = [(client, error(exc.code, exc.message, exc.ref))]
                        server.send_notifications(notes)
        except (OSError, ConnectionError) as exc:
            log.info("Connection closed session=%s (%s)", client.session_id[:8], exc)
        finally:
            with server.dispatch_lock:
                try:
                    server.send_notifications(server.manager.unregister(client))
                except Exception:
                    log.exception("Failed to clean up disconnected session=%s", client.session_id[:8])
            log.info("Disconnected session=%s", client.session_id[:8])


class BattleShipTCPServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True
    daemon_threads = True
    block_on_close = False

    def __init__(self, config: Config, database_module):
        self.config = config
        self.manager = SessionManager(database_module)
        self.db_error_class = getattr(database_module, "DbError", type("_NeverDbError", (Exception,), {}))
        self.dispatch_lock = Lock()  # Keep cross-client event order coherent, including ACK/GAME_START.
        self._capacity = BoundedSemaphore(config.max_clients)
        super().__init__((config.host, config.port), BattleShipHandler)

    def process_request(self, request: socket.socket, client_address: tuple) -> None:
        if not self._capacity.acquire(blocking=False):
            log.warning("Connection limit reached: %s", client_address)
            try:
                request.sendall(encode_message({"type": "ERROR", "seq": 1, "data": {
                    "code": "SERVER_ERROR", "message": "Server đạt giới hạn kết nối"}}))
            except OSError:
                pass
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, client_address)
        except BaseException:
            self._capacity.release()
            raise

    def process_request_thread(self, request: socket.socket, client_address: tuple) -> None:
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._capacity.release()

    @staticmethod
    def send_notifications(notes: list[Notification]) -> None:
        for target, msg in notes:
            try:
                target.send(msg)
            except (OSError, ConnectionError):
                log.warning("Delivery failed type=%s session=%s", msg["type"], target.session_id[:8])
                # Wake the handler so unregister() runs (and notifies the opponent).
                try:
                    target.conn.shutdown(socket.SHUT_RDWR)
                except OSError:
                    pass


def main() -> None:
    from database import database as db

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    config = Config.from_env()
    with BattleShipTCPServer(config, db) as server:
        log.info("BattleShip protocol v1 listening on %s:%d", *server.server_address)
        try:
            server.serve_forever(poll_interval=0.25)
        except KeyboardInterrupt:
            log.info("Shutdown requested")
        finally:
            server.shutdown()


if __name__ == "__main__":
    main()
