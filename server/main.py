from __future__ import annotations

import logging
import socket
import socketserver
from threading import BoundedSemaphore

from .config import Config
from .dispatcher import dispatch
from .protocol import ProtocolError, decode_message, error, event
from .session_manager import ClientSession, Notification, SessionManager

log = logging.getLogger("battleship.server")


class BattleShipHandler(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        server: BattleShipTCPServer = self.server  # type: ignore[assignment]
        conn: socket.socket = self.request
        conn.settimeout(server.config.idle_timeout)
        client = ClientSession(conn=conn)
        server.manager.register(client)
        log.info("Connected %s from %s", client.session_id[:8], self.client_address)
        try:
            client.send(event("WELCOME", {"session_id": client.session_id, "protocol_version": 1}))
            # A bounded readline is essential; recv() is NOT a message boundary.
            with conn.makefile("rb") as reader:
                while True:
                    try:
                        line = reader.readline(server.config.max_line_bytes + 1)
                    except socket.timeout:
                        log.info("Idle timeout: %s", client.session_id[:8])
                        break
                    if not line:
                        break
                    if len(line) > server.config.max_line_bytes:
                        client.send(error("MESSAGE_TOO_LARGE", "Line exceeds server limit"))
                        break  # Discard the connection: remaining bytes may be a partial frame.
                    if not line.endswith(b"\n"):
                        client.send(error("BAD_FRAME", "JSON message must end with newline"))
                        break
                    request_id = None
                    try:
                        message = decode_message(line)
                        request_id = message.get("request_id")
                        notes = dispatch(client, message, server.manager)
                    except ProtocolError as exc:
                        notes = [(client, error(exc.code, exc.message, request_id))]
                    server.send_notifications(notes)
        except (OSError, ConnectionError) as exc:
            log.info("Transport closed %s: %s", client.session_id[:8], exc)
        finally:
            server.send_notifications(server.manager.unregister(client))
            log.info("Disconnected %s", client.session_id[:8])


class BattleShipTCPServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    allow_reuse_address = True
    daemon_threads = True
    block_on_close = False

    def __init__(self, config: Config):
        self.config = config
        self.manager = SessionManager()
        self._capacity = BoundedSemaphore(config.max_clients)
        super().__init__((config.host, config.port), BattleShipHandler)

    def process_request(self, request: socket.socket, client_address: tuple) -> None:
        if not self._capacity.acquire(blocking=False):
            log.warning("Rejecting connection %s: capacity exceeded", client_address)
            try:
                request.sendall(b'{"type":"ERROR","payload":{"code":"SERVER_BUSY","message":"Too many clients"}}\n')
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
            except OSError:
                log.info("Could not deliver %s to %s", msg.get("type"), target.session_id[:8])


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    config = Config.from_env()
    with BattleShipTCPServer(config) as server:
        host, port = server.server_address
        log.info("BattleShip demo TCP server listening on %s:%s", host, port)
        log.warning("Demo matchmaking only: auth, game rules, and DB are not implemented")
        try:
            server.serve_forever(poll_interval=0.25)
        except KeyboardInterrupt:
            log.info("Shutdown requested")
        finally:
            server.shutdown()


if __name__ == "__main__":
    main()


