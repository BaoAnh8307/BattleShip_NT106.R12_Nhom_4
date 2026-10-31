import threading
import pytest

from logic.exceptions import (
    AlreadyFiredError,
    BadStateError,
    InvalidParamsError,
    LogicError,
    NotPlacedError,
    NotYourTurnError,
)
from logic.game_session import FireResult, GameSession


def sample_fleet():
    return [
        {"name": "CARRIER", "x": 0, "y": 0, "dir": "H"},
        {"name": "BATTLESHIP", "x": 0, "y": 1, "dir": "H"},
        {"name": "CRUISER", "x": 0, "y": 2, "dir": "H"},
        {"name": "SUBMARINE", "x": 0, "y": 3, "dir": "H"},
        {"name": "DESTROYER", "x": 0, "y": 4, "dir": "H"},
    ]


def setup_started_game(p1="alice", p2="bob", first_turn="alice"):
    session = GameSession(p1, p2, first_turn=first_turn)
    session.place_ships(p1, sample_fleet())
    session.place_ships(p2, sample_fleet())
    assert not session.ready(p1)
    assert session.ready(p2)
    assert session.state == GameSession.STATE_PLAYING
    return session


def test_exceptions_repr_and_classes():
    err = LogicError("CODE_X", "Custom msg")
    assert "LogicError" in repr(err)
    assert "CODE_X" in repr(err)

    params_err = InvalidParamsError("Invalid p")
    assert params_err.code == "INVALID_PARAMS"


def test_init_game_session():
    session = GameSession("p1", "p2", first_turn="p1")
    assert session.player1_id == "p1"
    assert session.player2_id == "p2"
    assert session.current_turn == "p1"
    assert session.state == GameSession.STATE_PLACING
    assert session.total_shots == 0
    assert not session.is_game_over

    # Hai người cùng ID -> lỗi
    with pytest.raises(LogicError):
        GameSession("p1", "p1")


def test_invalid_player_actions():
    session = GameSession("p1", "p2")
    with pytest.raises(LogicError):
        session.get_opponent("hacker")
    with pytest.raises(LogicError):
        session.get_board("hacker")


def test_place_ships_and_ready():
    session = GameSession("p1", "p2")

    # Bấm ready khi chưa đặt tàu -> NOT_PLACED
    with pytest.raises(NotPlacedError):
        session.ready("p1")

    # Đặt tàu thành công
    session.place_ships("p1", sample_fleet())
    # Có thể đặt lại khi chưa ready
    session.place_ships("p1", sample_fleet())

    # Sẵn sàng P1
    assert not session.ready("p1")

    # Đã ready thì không được sửa tàu
    with pytest.raises(BadStateError):
        session.place_ships("p1", sample_fleet())

    # P2 đặt tàu và ready -> Bắt đầu ván
    session.place_ships("p2", sample_fleet())
    assert session.ready("p2")
    assert session.state == GameSession.STATE_PLAYING

    # Đang chơi mà gọi place_ships hay ready -> BadStateError
    with pytest.raises(BadStateError):
        session.place_ships("p1", sample_fleet())
    with pytest.raises(BadStateError):
        session.ready("p1")


def test_turn_order_and_miss_switches_turn():
    session = setup_started_game(p1="alice", p2="bob", first_turn="alice")

    # Bob cố bắn khi chưa tới lượt
    with pytest.raises(NotYourTurnError):
        session.fire("bob", 9, 9)

    # Alice bắn trượt tại (9, 9)
    res = session.fire("alice", 9, 9)
    assert res.result == "MISS"
    assert res.next_turn == "bob"
    assert not res.game_over
    assert session.total_shots == 1

    # Kiểm tra dịch sang gói tin cho từng người
    alice_view = res.for_player("alice")
    bob_view = res.for_player("bob")
    assert alice_view["next_turn"] == "opponent"
    assert bob_view["next_turn"] == "you"

    # Alice bắn tiếp sẽ bị lỗi vì đã chuyển lượt cho Bob
    with pytest.raises(NotYourTurnError):
        session.fire("alice", 8, 8)


def test_hit_keeps_turn():
    # Luật: HIT_KEEPS_TURN = True (bắn trúng được bắn tiếp)
    session = setup_started_game(p1="alice", p2="bob", first_turn="alice")

    # Alice bắn trúng CARRIER của Bob tại (0, 0)
    res1 = session.fire("alice", 0, 0)
    assert res1.result == "HIT"
    assert res1.next_turn == "alice"
    assert res1.for_player("alice")["next_turn"] == "you"

    # Alice bắn tiếp trúng tại (1, 0)
    res2 = session.fire("alice", 1, 0)
    assert res2.result == "HIT"
    assert res2.next_turn == "alice"

    # Alice bắn trượt tại (9, 9) -> Mới mất lượt
    res3 = session.fire("alice", 9, 9)
    assert res3.result == "MISS"
    assert res3.next_turn == "bob"


def test_invalid_shot_does_not_lose_turn():
    # Luật: Bắn ngoài bảng hoặc ô đã bắn không mất lượt (mục 9 quyết định 3 PROTOCOL.md)
    session = setup_started_game(p1="alice", p2="bob", first_turn="alice")

    with pytest.raises(Exception):
        session.fire("alice", 99, 99)

    # Vẫn là lượt của Alice
    assert session.current_turn == "alice"

    # Alice bắn hợp lệ vào (9, 8) trượt
    res = session.fire("alice", 9, 8)
    assert res.result == "MISS"
    assert session.current_turn == "bob"

    # Bob bắn vào ô (9, 9) trượt
    session.fire("bob", 9, 9)
    assert session.current_turn == "alice"

    # Alice cố bắn lại vào (9, 8) -> ALREADY_FIRED
    with pytest.raises(AlreadyFiredError):
        session.fire("alice", 9, 8)

    # Alice vẫn giữ lượt
    assert session.current_turn == "alice"


def test_full_game_win_all_ships_sunk():
    session = setup_started_game(p1="alice", p2="bob", first_turn="alice")
    bob_board = session.get_board("bob")

    # Alice bắn trúng liên tiếp mọi ô của tất cả tàu của Bob
    all_cells = []
    for ship in bob_board.ships.values():
        all_cells.extend(ship.cells)

    last_res = None
    for cell in all_cells:
        last_res = session.fire("alice", cell.x, cell.y)

    assert last_res is not None
    assert last_res.game_over is True
    assert last_res.result == "SUNK"
    assert last_res.next_turn is None
    assert last_res.for_player("alice")["next_turn"] is None
    assert session.state == GameSession.STATE_FINISHED
    assert session.is_game_over
    assert session.winner == "alice"
    assert session.loser == "bob"
    assert session.end_reason == "ALL_SHIPS_SUNK"
    assert session.total_shots == 17  # 5 + 4 + 3 + 3 + 2 = 17 ô

    # Thử bắn sau khi hết ván -> BAD_STATE
    with pytest.raises(BadStateError):
        session.fire("alice", 9, 9)

    # Thử surrender sau khi hết ván -> BAD_STATE
    with pytest.raises(BadStateError):
        session.surrender("alice")

    # Disconnect sau khi hết ván -> NONE
    assert session.disconnect("alice") == {"action": "NONE"}


def test_surrender_during_placing():
    session = GameSession("alice", "bob")
    res = session.surrender("alice")
    assert res["action"] == "CANCELLED"
    assert res["reason"] == "OPPONENT_LEFT"
    assert res["cancelled_by"] == "alice"
    assert session.state == GameSession.STATE_CANCELLED
    assert session.winner is None
    assert session.is_game_over


def test_disconnect_during_placing():
    session = GameSession("alice", "bob")
    res = session.disconnect("alice")
    assert res["action"] == "CANCELLED"
    assert res["reason"] == "OPPONENT_LEFT"
    assert session.state == GameSession.STATE_CANCELLED


def test_surrender_during_playing():
    session = setup_started_game(p1="alice", p2="bob", first_turn="alice")
    res = session.surrender("alice")
    assert res["action"] == "GAME_OVER"
    assert res["game_over"] is True
    assert res["winner"] == "bob"
    assert res["loser"] == "alice"
    assert res["reason"] == "SURRENDER"
    assert session.state == GameSession.STATE_FINISHED


def test_disconnect_during_playing():
    session = setup_started_game(p1="alice", p2="bob", first_turn="alice")
    res = session.disconnect("bob")
    assert res["action"] == "GAME_OVER"
    assert res["game_over"] is True
    assert res["winner"] == "alice"
    assert res["loser"] == "bob"
    assert res["reason"] == "DISCONNECT"
    assert session.state == GameSession.STATE_FINISHED


def test_fire_result_for_player_branches():
    fr = FireResult(
        x=1,
        y=2,
        result="HIT",
        sunk_ship=None,
        sunk_cells=[],
        next_turn="p1",
        game_over=False,
        shooter="p1",
        target="p2",
    )
    assert fr.for_player("p1")["next_turn"] == "you"
    assert fr.for_player("p2")["next_turn"] == "opponent"


def test_multithread_lock_safety():
    session = setup_started_game(p1="alice", p2="bob", first_turn="alice")
    errors = []

    def shoot(p, x, y):
        try:
            session.fire(p, x, y)
        except Exception as e:
            errors.append(e)

    # 2 luồng cùng bắn
    t1 = threading.Thread(target=shoot, args=("alice", 0, 0))
    t2 = threading.Thread(target=shoot, args=("bob", 0, 0))
    t1.start()
    t2.start()
    t1.join()
    t2.join()

    # Nhờ lock, 1 luồng được xử lý hợp lệ và 1 luồng bị từ chối nếu không đúng lượt, không crash
    assert session.total_shots >= 1
