# Lop DB - Battleship (NT106)

Phan cua Duy. Server (Kien) chi viec `import database` roi goi ham, khong phai viet SQL.

## Cai dat

1. Cai SQL Server + ODBC Driver 18 cho SQL Server.
2. Chay `schema.sql` trong SSMS de tao database `Battleship` (2 bang: `Users`, `Matches`).
3. `pip install -r requirements.txt`
4. Sua `CONN_STR` trong `database.py` cho dung may minh, hoac dat bien moi truong `BATTLESHIP_DB`.
5. Chay `python test_database.py` de kiem tra. Neu in ra bao nhieu dong "ok" nhu ben duoi la xong.

## Cac ham

| Ham | Goi khi nao | Tra ve |
|---|---|---|
| `register(username, password)` | `REGISTER` hop le | `user_id` |
| `login(username, password)` | `LOGIN` | `{"user_id", "username"}` |
| `create_match(p1_id, p2_id)` | `GAME_START` | `match_id` |
| `finish_match(match_id, winner_id, reason, total_shots)` | `GAME_OVER` | khong |
| `get_stats(user_id)` | `GET_STATS` | `{"played", "wins", "losses"}` |
| `get_history(user_id, limit=20)` | `GET_HISTORY` | list cac van (khop `HISTORY.matches`) |

`create_match` va `finish_match` la ten minh tu dat vi protocol muc 8.4 chua dat ten. Ai muon doi thi noi minh.

## Loi

Chi co 2 loi nghiep vu, nem ra `database.DbError`, doc `e.code`:

- `USERNAME_TAKEN` - dang ky trung ten
- `AUTH_FAILED` - sai ten hoac sai mat khau (co y khong noi ro cai nao)

Con lai (mat ket noi SQL Server...) la exception cua pyodbc, server bat roi tra `SERVER_ERROR` va log.

## Luu y cho Kien

- Kiem tra do dai/ky tu username, mat khau (`INVALID_USERNAME`, `WEAK_PASSWORD`) la viec cua server, truoc khi goi DB.
- `ALREADY_LOGGED_IN` cung do server quan ly (danh sach nguoi dang online), DB khong biet.
- Luu `match_id` ma `create_match` tra ve trong session de lat nua goi `finish_match`.
- Van bi `DISCONNECT` van phai goi `finish_match` voi `reason="DISCONNECT"`, `winner_id` la nguoi con lai (muc 8.5).
- `MATCH_CANCELLED` truoc `GAME_START` thi **khong** goi `create_match`.
- `finish_match` goi 2 lan khong sao, lan sau bi bo qua. Phong truong hop hai thread cung ket thuc van.
- `winner_id` truyen vao la `user_id` (so), khong phai ten.

## Ghi chu thiet ke

- Thong ke tinh truc tiep tu bang `Matches`, khong luu cot thang/thua rieng, nen khong bi lech so.
- Mat khau bam PBKDF2-SHA256 co salt rieng tung nguoi. Luu y mat khau van di qua TCP khong ma hoa (da ghi o protocol muc 8.2, nho ghi vao bao cao).
- Chi van da ket thuc (`ended_at` khac NULL) moi tinh vao stats va history.
- Ten dang nhap khong phan biet hoa thuong (`Khoi01` va `khoi01` la mot).

## Chua lam

- Xoa/doi mat khau, xoa tai khoan.
- Connection pool (hien moi lan goi mo ket noi moi, voi quy mo bai tap la du).
