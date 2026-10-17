# BattleShip – NT106 (Lập trình mạng căn bản)

Game Battleship 2 người chơi qua mạng. Client C# WinForms, server Python, giao tiếp bằng TCP.

## Thành viên

| MSSV | Họ tên | Vai trò |
|---|---|---|
| 25520053 | Huỳnh Trần Duy Bảo Anh | Quản lý code + GUI |
| 25520932 | Bùi Trọng Kiên | Server (network) + quản lý code |
| 25520440 | Phạm Lê Minh Duy | Cơ sở dữ liệu |
| 25520906 | Nguyễn Đăng Khôi | Logic game |
| 25521161 | Nguyễn Hoài Nam | Client (socket) |

## Công nghệ đã chốt

| Hạng mục | Lựa chọn |
|---|---|
| Giao thức mạng | TCP, port mặc định 5000 (đặt trong config) |
| Khung message | JSON mỗi dòng, kết thúc bằng `\n` |
| Server | Python 3.11+ (1 thread/client, dùng `Lock` cho trạng thái chung) |
| Cơ sở dữ liệu | SQLite (`sqlite3`), hash mật khẩu bằng `hashlib.pbkdf2_hmac` |
| Client | C# WinForms, .NET 10 |
| Giao diện | ReaLTaiizor (tổng quan, miễn phí, mã nguồn mở) + GDI+ (`BoardPanel` vẽ bàn cờ). Không dùng Guna.UI2 vì yêu cầu kích hoạt license |
| Luật chơi | Server giữ; server không gửi vị trí tàu cho đối thủ |
| Ghép trận | Hàng chờ tự động (mã phòng: làm thêm khi chơi qua internet) |
| Khi bắn trúng | Bắn tiếp (`HIT_KEEPS_TURN = True`) |
| Tàu đặt sát nhau | Cho phép; chỉ cấm chồng nhau và ra ngoài bảng |
| Client nhận dữ liệu | `async/await` với `TcpClient`/`NetworkStream`; cập nhật UI qua `Invoke`/`SynchronizationContext` |
| Mất kết nối giữa ván | Đối thủ thoát = thắng; dùng PING/PONG để phát hiện (reconnect: làm thêm nếu còn thời gian) |


## Cấu trúc repo

```
BattleShip/
├── docs/          PROTOCOL.md, DB_SCHEMA.sql, FLOW.md, TEST_CHECKLIST.md, wireframes/
├── server/        main.py, dispatcher.py, session_manager.py, config.py, tests/   (Kiên)
├── logic/         board.py, ship.py, game_session.py, tests/                       (Khôi)
├── database/      db.py, schema.sql, tests/                                        (Duy)
├── tests/
│   └── integration/   test sau khi ghép (server + logic + db + client giả)
├── tools/         fake_client.py (Kiên)
├── client/
│   ├── BattleShip.sln         (duy nhất một solution)
│   ├── BattleShip/            (duy nhất một project WinForms: Forms/, Controls/, Network/)
│   └── BattleShip.Tests/      (Nam)
└── .github/       workflows/, CODEOWNERS, pull_request_template.md
```

## Chạy dự án

### Server (Python)
```bash
python -m venv .venv
# Windows: .venv\Scripts\activate    |   macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
python -m server.main
```

### Client (C#)
1. Mở `client/BattleShip.sln` bằng Visual Studio (cần hỗ trợ .NET 10: VS 2022 bản 17.14 trở lên hoặc VS 2026) và cài .NET 10 SDK.
2. Restore NuGet, chọn project `BattleShip` làm Startup Project, nhấn F5.
3. Đổi địa chỉ server trong file cấu hình của client (không sửa code trực tiếp).

### Chạy test
```bash
pytest                      # toàn bộ test Python (chạy từ thư mục gốc repo)
pytest tests/integration    # chỉ test tích hợp
dotnet test client          # test phía C#
```

## Quy trình làm việc (Git)

- Nhánh `main` bị khóa: chỉ merge qua Pull Request, cần ít nhất 1 người review, test phải xanh.
- Đặt tên nhánh: `feature/<tên>-<việc>`, ví dụ `feature/khoi-board`.
- Merge `main` về nhánh của mình mỗi ngày.
- Việc đụng `docs/PROTOCOL.md`: cả nhóm phải duyệt. Muốn đổi giao thức thì sửa tài liệu trước, báo nhóm, rồi mới sửa code.
- Chỉ sửa thư mục của mình. Cần sửa thư mục người khác thì nhắn họ hoặc mở PR riêng để họ review.
- Client: **một solution, một project**. Mỗi form một người phụ trách; ai thêm file vào `.csproj` thì commit và merge ngay.
- Không commit `bin/`, `obj/`, `.vs/`, `*.db`, file cấu hình riêng từng máy (dùng file `*.example`).

## Mốc tiến độ

| Mốc | Tuần | Kết quả |
|---|---|---|
| Thiết kế chốt | 1 | PROTOCOL.md, DB_SCHEMA.sql, chữ ký hàm, wireframe được duyệt |
| M1 | 3 | Đăng ký/đăng nhập thật từ GUI, lưu DB |
| M2 | 4 | 2 client ghép cặp, đặt tàu, Sẵn sàng vào ván |
| M3 | 5 | Chơi trọn 1 ván trên 2 máy trong LAN, lưu DB |
| M4 | 7 | Feature freeze |
| Nộp bài | 8 | Báo cáo, hướng dẫn chạy, demo |

## Tài liệu

- [Giao thức](docs/PROTOCOL.md)
- [Lược đồ CSDL](docs/DB_SCHEMA.sql)
- [Luồng một ván](docs/FLOW.md)
- [Checklist kiểm thử](docs/TEST_CHECKLIST.md)
