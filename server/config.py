
from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Config:
    host: str = "127.0.0.1"  # Explicitly set 0.0.0.0 when testing across LAN.
    port: int = 5000
    idle_timeout: float = 75.0
    max_line_bytes: int = 16_384
    max_clients: int = 32

    @classmethod
    def from_env(cls) -> "Config":
        config = cls(
            host=os.getenv("BATTLESHIP_HOST", "127.0.0.1"),
            port=int(os.getenv("BATTLESHIP_PORT", "5000")),
            idle_timeout=float(os.getenv("BATTLESHIP_IDLE_TIMEOUT", "75")),
            max_line_bytes=int(os.getenv("BATTLESHIP_MAX_LINE_BYTES", "16384")),
            max_clients=int(os.getenv("BATTLESHIP_MAX_CLIENTS", "32")),
        )
        if not (0 <= config.port <= 65535):
            raise ValueError("BATTLESHIP_PORT must be between 0 and 65535")
        if config.idle_timeout <= 0 or config.max_line_bytes < 128 or config.max_clients < 1:
            raise ValueError("Invalid timeout, line limit, or max clients")
        return config






