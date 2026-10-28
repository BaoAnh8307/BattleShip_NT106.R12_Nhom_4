"""
database.py - lop DB cho server Battleship (NT106)
Nguoi lam: Duy

Server (Kien) chi can import file nay roi goi ham, khong can biet SQL.
Loi nghiep vu (trung ten, sai mat khau) nem ra DbError, code trung voi ma loi trong PROTOCOL.
"""
import hashlib
import hmac
import os

import pyodbc

# Chuoi ket noi. Moi nguoi sua lai cho may minh, hoac dat bien moi truong BATTLESHIP_DB.
CONN_STR = os.environ.get(
    "BATTLESHIP_DB",
    "DRIVER={ODBC Driver 18 for SQL Server};"
    "SERVER=localhost;DATABASE=Battleship;"
    "Trusted_Connection=yes;TrustServerCertificate=yes;",
)

ITERATIONS = 200_000  # so vong PBKDF2


class DbError(Exception):
    """e.code la USERNAME_TAKEN hoac AUTH_FAILED."""

    def __init__(self, code):
        super().__init__(code)
        self.code = code


def _connect():
    # moi lan goi mo 1 ket noi moi cho don gian, server nhieu thread nen khong dung chung
    return pyodbc.connect(CONN_STR)


def _hash(password, salt):
    return hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, ITERATIONS)


def _iso(dt):
    # protocol muon dang 2026-11-16T09:30:00Z
    if dt is None:
        return None
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


# ---------- tai khoan ----------

def register(username, password):
    """Tao tai khoan, tra ve user_id. Trung ten -> DbError('USERNAME_TAKEN').
    Viec kiem tra do dai/ky tu (INVALID_USERNAME, WEAK_PASSWORD) la cua server, o day khong kiem tra lai."""
    salt = os.urandom(16)
    pw_hash = _hash(password, salt)
    with _connect() as conn:
        cur = conn.cursor()
        try:
            cur.execute(
                "INSERT INTO Users (username, password_hash, salt) "
                "OUTPUT INSERTED.user_id VALUES (?, ?, ?)",
                username, pw_hash, salt,
            )
            user_id = cur.fetchone()[0]
        except pyodbc.IntegrityError:
            raise DbError("USERNAME_TAKEN")
        conn.commit()
    return user_id


def login(username, password):
    """Dung -> {'user_id': .., 'username': ..}. Sai ten hoac sai mat khau -> DbError('AUTH_FAILED')
    (cố ý không phân biệt hai trường hợp, theo protocol)."""
    with _connect() as conn:
        row = conn.cursor().execute(
            "SELECT user_id, username, password_hash, salt FROM Users WHERE username = ?",
            username,
        ).fetchone()

    if row is None:
        _hash(password, os.urandom(16))  # tinh thu cho thoi gian xu ly gan giong nhau
        raise DbError("AUTH_FAILED")

    if not hmac.compare_digest(_hash(password, bytes(row.salt)), bytes(row.password_hash)):
        raise DbError("AUTH_FAILED")

    return {"user_id": row.user_id, "username": row.username}


# ---------- van dau ----------

def create_match(player1_id, player2_id):
    """Goi luc GAME_START. Tra ve match_id, server nho giu lai de goi finish_match."""
    with _connect() as conn:
        cur = conn.cursor()
        cur.execute(
            "INSERT INTO Matches (player1_id, player2_id) "
            "OUTPUT INSERTED.match_id VALUES (?, ?)",
            player1_id, player2_id,
        )
        match_id = cur.fetchone()[0]
        conn.commit()
    return match_id


def finish_match(match_id, winner_id, reason, total_shots):
    """Goi luc GAME_OVER (ca SURRENDER va DISCONNECT).
    reason: 'ALL_SHIPS_SUNK' | 'SURRENDER' | 'DISCONNECT'.
    Goi 2 lan cung khong sao, lan sau khong ghi de (chi cap nhat khi ended_at con NULL)."""
    with _connect() as conn:
        conn.cursor().execute(
            "UPDATE Matches SET winner_id = ?, end_reason = ?, total_shots = ?, "
            "ended_at = SYSUTCDATETIME() "
            "WHERE match_id = ? AND ended_at IS NULL",
            winner_id, reason, total_shots, match_id,
        )
        conn.commit()


# ---------- thong ke ----------

def get_stats(user_id):
    """Tra ve {'played', 'wins', 'losses'} - dung luon lam data cho STATS. Chi dem van da ket thuc."""
    with _connect() as conn:
        row = conn.cursor().execute(
            "SELECT COUNT(*), COALESCE(SUM(CASE WHEN winner_id = ? THEN 1 ELSE 0 END), 0) "
            "FROM Matches "
            "WHERE (player1_id = ? OR player2_id = ?) AND ended_at IS NOT NULL",
            user_id, user_id, user_id,
        ).fetchone()
    played, wins = row[0], row[1]
    return {"played": played, "wins": wins, "losses": played - wins}


def get_history(user_id, limit=20):
    """Tra ve list dict de nhet vao HISTORY.matches, moi nhat truoc. limit mac dinh 20, toi da 50."""
    limit = max(1, min(int(limit), 50))
    with _connect() as conn:
        rows = conn.cursor().execute(
            "SELECT TOP (?) m.match_id, u.username, "
            "       CASE WHEN m.winner_id = ? THEN 'WIN' ELSE 'LOSE' END, "
            "       m.total_shots, m.started_at, m.ended_at "
            "FROM Matches m "
            "JOIN Users u ON u.user_id = "
            "     CASE WHEN m.player1_id = ? THEN m.player2_id ELSE m.player1_id END "
            "WHERE (m.player1_id = ? OR m.player2_id = ?) AND m.ended_at IS NOT NULL "
            "ORDER BY m.ended_at DESC",
            limit, user_id, user_id, user_id, user_id,
        ).fetchall()

    return [
        {
            "match_id": r[0],
            "opponent": r[1],
            "result": r[2],
            "total_shots": r[3],
            "started_at": _iso(r[4]),
            "ended_at": _iso(r[5]),
        }
        for r in rows
    ]
