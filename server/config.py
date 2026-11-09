from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Config:
    host: str = "127.0.0.1"  # Đổi 0.0.0.0 nếu cần thử nghiệm qua LAN (TCP chưa dùng TLS).
    port: int = 5000
    idle_timeout: float = 30.0
    max_line_bytes: int = 64 * 1024
    max_clients: int = 32

    @classmethod
    def from_env(cls) -> "Config":
        config = cls(
            host=os.getenv("BATTLESHIP_HOST", "127.0.0.1"),
            port=int(os.getenv("BATTLESHIP_PORT", "5000")),
            idle_timeout=float(os.getenv("BATTLESHIP_IDLE_TIMEOUT", "30")),
            max_line_bytes=int(os.getenv("BATTLESHIP_MAX_LINE_BYTES", str(64 * 1024))),
            max_clients=int(os.getenv("BATTLESHIP_MAX_CLIENTS", "32")),
        )
        if not (0 <= config.port <= 65535):
            raise ValueError("BATTLESHIP_PORT không hợp lệ")
        if config.idle_timeout <= 0 or config.max_line_bytes < 128 or config.max_clients < 1:
            raise ValueError("Timeout, max_line_bytes hoặc max_clients không hợp lệ")
        return config
