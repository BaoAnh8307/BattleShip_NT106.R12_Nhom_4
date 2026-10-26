# PROTOCOL – Giao thức BattleShip (NT106)

> **Trạng thái:** **v1.0.** Nếu phát hiện vấn đề thì nhóm thống nhất lại sau.
> **Muốn đổi giao thức:** sửa file này trước (qua Pull Request, cả nhóm duyệt), báo cả nhóm, rồi mới sửa code.

| Người | Dùng file này để |
|---|---|
| Kiên | Cài server: dispatcher, máy trạng thái, kiểm tra hợp lệ |
| Nam | Cài `NetworkClient`: gửi/nhận, parse, bắn event |
| Bảo Anh | Dựng `MockNetworkClient` và GUI theo đúng message |
| Khôi | Biết server gọi logic ra sao (mục 8.1) |
| Duy | Biết khi nào server gọi hàm DB (mục 8.4) |

---

## 1. Tổng quan

- Client C# WinForms, server Python, nói chuyện qua **TCP**.
- **Server giữ luật chơi và vị trí tàu của cả hai người.** Client chỉ gửi lệnh và hiển thị kết quả server trả về.
- Server **không bao giờ** gửi vị trí tàu của người này cho người kia, trừ danh sách ô của tàu **vừa bị chìm** (đó là thông tin công khai theo luật).

## 2. Truyền tải và khung message

| Mục | Quy định |
|---|---|
| Giao vận | TCP, port mặc định `5000` (đặt trong file cấu hình, không hard-code) |
| Khung | **Mỗi message là một dòng JSON, kết thúc bằng `\n`** |
| Mã hóa | UTF-8 |
| JSON | Gọn một dòng, **không chứa ký tự xuống dòng thật** (xuống dòng trong chuỗi phải là `\n` đã escape) |
| Độ dài tối đa một dòng | 64 KB. Vượt quá: server gửi `ERROR LINE_TOO_LONG` rồi **đóng kết nối** |
| Phiên bản | `PROTOCOL_VERSION = 1`, gửi trong `REGISTER` và `LOGIN` |

### 2.1 Phong bì (envelope)

Mọi message đều có dạng:

```json
{"type":"FIRE","seq":12,"data":{"x":3,"y":7}}
```

| Trường | Bắt buộc | Ý nghĩa |
|---|---|---|
| `type` | có | Tên message, **chữ HOA**, xem mục 5 |
| `seq` | có | Số thứ tự message **do bên gửi đánh**, mỗi bên tự đếm từ `1` và tăng dần. Chỉ để debug và log |
| `ref` | chỉ ở phản hồi | `seq` của message client đã gửi mà server đang trả lời. Message server tự đẩy xuống (như `OPPONENT_FIRED`) thì **không có** `ref` |
| `data` | có | Object chứa nội dung. Message không có nội dung thì để `{}` |

Ví dụ phản hồi:

```json
{"type":"FIRE_RESULT","seq":40,"ref":12,"data":{"x":3,"y":7,"result":"HIT","sunk_ship":null,"sunk_cells":[],"next_turn":"you","game_over":false}}
```

### 2.2 Giữ kết nối sống (PING/PONG)

- Client gửi `PING` mỗi **10 giây** khi rảnh, server trả `PONG`.
- **Bất kỳ** message nào nhận được đều làm mới bộ đếm. Server không nhận gì trong **30 giây** thì coi là mất kết nối và đóng socket.

## 3. Quy ước dữ liệu

### 3.1 Tọa độ

- Bảng **10x10**. `x` là **cột** (0 → 9, trái sang phải), `y` là **hàng** (0 → 9, trên xuống dưới).
- Gốc `(0,0)` ở **góc trên trái**.

### 3.2 Tàu

Mỗi người đặt đúng **5 tàu**, mỗi tên dùng đúng một lần:

| `name` (mã dùng trong JSON) | Độ dài | Gợi ý hiển thị |
|---|---|---|
| `CARRIER` | 5 | Tàu sân bay |
| `BATTLESHIP` | 4 | Thiết giáp hạm |
| `CRUISER` | 3 | Tàu tuần dương |
| `SUBMARINE` | 3 | Tàu ngầm |
| `DESTROYER` | 2 | Tàu khu trục |

Có **hai tàu dài 3** nên bắt buộc phân biệt bằng `name`, không phân biệt bằng độ dài.

### 3.3 Cách đặt tàu

Mỗi tàu gửi `name`, ô đầu `(x, y)` và hướng `dir`:

| `dir` | Các ô tàu chiếm |
|---|---|
| `"H"` (ngang) | `(x, y)`, `(x+1, y)`, … `(x+độ dài-1, y)` |
| `"V"` (dọc) | `(x, y)`, `(x, y+1)`, … `(x, y+độ dài-1)` |

Ô đầu luôn là ô **trái nhất** (với `H`) hoặc **trên cùng** (với `V`).

**Quy tắc hợp lệ** (server kiểm tra, lỗi trả `INVALID_SHIPS`):
1. Đủ đúng 5 tàu, đủ 5 tên, không trùng tên.
2. Không có ô nào nằm ngoài bảng.
3. Không có hai tàu chồng lên cùng một ô.
4. Tàu **được phép đặt sát nhau** (chạm cạnh hoặc góc).

### 3.4 Trạng thái ô và kết quả phát bắn

Hai khái niệm này **khác nhau**, đừng nhầm:

| Khái niệm | Giá trị | Nằm ở đâu |
|---|---|---|
| **Trạng thái ô** | `EMPTY`, `SHIP`, `HIT`, `MISS` | Bên trong bảng (server và client tự giữ), **không gửi qua mạng** |
| **Kết quả phát bắn** | `MISS`, `HIT`, `SUNK` | Gửi qua mạng trong `FIRE_RESULT` và `OPPONENT_FIRED` |

Cách ánh xạ:
- `MISS` → ô đó thành `MISS`.
- `HIT` → ô đó thành `HIT`.
- `SUNK` → ô đó thành `HIT`, **cộng thêm** `sunk_cells` để client tô nổi cả tàu vừa chìm.

**Bảng theo dõi** (bảng bắn vào đối phương) chỉ có `EMPTY` (chưa bắn, chưa biết), `HIT`, `MISS`. Client tự dựng bảng này từ `FIRE_RESULT`. **Server không gửi bảng.**

### 3.5 Định dạng khác

| Mục | Quy định |
|---|---|
| Thời gian | Chuỗi ISO 8601 UTC, ví dụ `"2026-11-16T09:30:00Z"` |
| Boolean | `true` / `false` |
| Giá trị không có | `null` (ví dụ `sunk_ship` khi chưa chìm) |
| Tên trường | `snake_case` |
| Tên `type`, giá trị enum | CHỮ HOA |

## 4. Máy trạng thái

Mỗi kết nối nằm ở đúng **một** trạng thái của server:

```
CONNECTED → AUTHED → IN_QUEUE → PLACING → READY → PLAYING → AUTHED
                ↑                  │         │        │
                └──── MATCH_CANCELLED / GAME_OVER ────┘
```

| Trạng thái | Ý nghĩa | Client được gửi |
|---|---|---|
| `CONNECTED` | Vừa kết nối, chưa đăng nhập | `REGISTER`, `LOGIN`, `PING` |
| `AUTHED` | Đã đăng nhập, ở sảnh | `FIND_MATCH`, `GET_STATS`, `GET_HISTORY`, `PING` |
| `IN_QUEUE` | Đang chờ ghép trận | `PING` |
| `PLACING` | Đã ghép cặp, đang đặt tàu | `PLACE_SHIPS`, `READY`, `SURRENDER`, `PING` |
| `READY` | Đã bấm sẵn sàng, chờ đối thủ | `SURRENDER`, `PING` |
| `PLAYING` | Đang bắn nhau | `FIRE`, `SURRENDER`, `PING` |

Gửi message **sai trạng thái** → `ERROR BAD_STATE` (không đổi trạng thái, không đóng kết nối).

Chuyển trạng thái:

| Từ | Sự kiện | Sang |
|---|---|---|
| `CONNECTED` | `LOGIN` thành công | `AUTHED` |
| `AUTHED` | `FIND_MATCH` | `IN_QUEUE` |
| `IN_QUEUE` | Ghép được đối thủ (`MATCH_FOUND`) | `PLACING` |
| `PLACING` | `READY` thành công | `READY` |
| `READY` | Cả hai đã `READY` (`GAME_START`) | `PLAYING` |
| `PLAYING` | `GAME_OVER` | `AUTHED` |
| `PLACING` / `READY` | Đối thủ rời (`MATCH_CANCELLED`) hoặc mình `SURRENDER` | `AUTHED` |

## 5. Danh sách message

### 5.1 Tổng hợp

| Hướng | `type` | Mục đích | Trạng thái |
|---|---|---|---|
| C→S | `REGISTER` | Đăng ký | `CONNECTED` |
| C→S | `LOGIN` | Đăng nhập | `CONNECTED` |
| C→S | `FIND_MATCH` | Vào hàng đợi | `AUTHED` |
| C→S | `PLACE_SHIPS` | Gửi vị trí tàu | `PLACING` |
| C→S | `READY` | Bấm Sẵn sàng | `PLACING` |
| C→S | `FIRE` | Bắn một ô | `PLAYING` |
| C→S | `SURRENDER` | Đầu hàng / rời trận | `PLACING`, `READY`, `PLAYING` |
| C→S | `GET_STATS` | Xem thống kê | `AUTHED` |
| C→S | `GET_HISTORY` | Xem lịch sử | `AUTHED` |
| C→S | `PING` | Giữ kết nối | mọi trạng thái |
| S→C | `ACK` (bổ sung) | Xác nhận lệnh thành công không có dữ liệu trả về | — |
| S→C | `LOGIN_OK` | Đăng nhập thành công | — |
| S→C | `ERROR` | Báo lỗi | — |
| S→C | `MATCH_FOUND` | Đã có đối thủ | — |
| S→C | `MATCH_CANCELLED` (bổ sung) | Trận bị hủy trước khi bắt đầu | — |
| S→C | `GAME_START` | Cả hai đã sẵn sàng | — |
| S→C | `FIRE_RESULT` | Kết quả phát bắn **của mình** | — |
| S→C | `OPPONENT_FIRED` | Đối thủ vừa bắn vào bảng **của mình** | — |
| S→C | `GAME_OVER` | Hết ván | — |
| S→C | `PONG`, `STATS`, `HISTORY` | Phản hồi các lệnh phụ | — |

> **Phần bổ sung so với lộ trình 8 tuần** (nhóm đã chấp nhận ở v1): hai type `ACK`, `MATCH_CANCELLED` và trường `ref`. Lý do ở mục 9, quyết định số 6.

### 5.2 Chi tiết từng message

#### `REGISTER` (C→S)

```json
{"type":"REGISTER","seq":1,"data":{"username":"khoi01","password":"matkhau123","version":1}}
```

| Trường | Quy tắc |
|---|---|
| `username` | 3–20 ký tự, chỉ gồm chữ không dấu, số, `_` |
| `password` | 6–64 ký tự |
| `version` | Phải bằng `PROTOCOL_VERSION` |

Thành công: `ACK`. Lỗi: `USERNAME_TAKEN`, `INVALID_USERNAME`, `WEAK_PASSWORD`, `UNSUPPORTED_VERSION`, `INVALID_PARAMS`. Đăng ký xong vẫn ở `CONNECTED`, client phải `LOGIN` riêng.

#### `LOGIN` (C→S) và `LOGIN_OK` (S→C)

```json
{"type":"LOGIN","seq":2,"data":{"username":"khoi01","password":"matkhau123","version":1}}
```
```json
{"type":"LOGIN_OK","seq":1,"ref":2,"data":{"user_id":7,"username":"khoi01"}}
```

Lỗi: `AUTH_FAILED` (sai tên **hoặc** sai mật khẩu, **không** nói rõ cái nào), `ALREADY_LOGGED_IN` (tài khoản đang đăng nhập ở kết nối khác), `UNSUPPORTED_VERSION`.

#### `FIND_MATCH` (C→S)

```json
{"type":"FIND_MATCH","seq":3,"data":{}}
```

Thành công: `ACK` (đã vào hàng đợi). Sau đó server tự gửi `MATCH_FOUND` khi ghép được. Mất kết nối khi đang chờ thì server xóa khỏi hàng đợi.

#### `MATCH_FOUND` (S→C)

```json
{"type":"MATCH_FOUND","seq":2,"data":{"opponent":"nam02","you_first":true}}
```

`you_first` cho biết **ai bắn trước**. Server chọn ngẫu nhiên, mỗi bên nhận giá trị ngược nhau.

#### `PLACE_SHIPS` (C→S)

```json
{"type":"PLACE_SHIPS","seq":4,"data":{"ships":[
  {"name":"CARRIER","x":0,"y":0,"dir":"H"},
  {"name":"BATTLESHIP","x":0,"y":2,"dir":"H"},
  {"name":"CRUISER","x":5,"y":4,"dir":"V"},
  {"name":"SUBMARINE","x":8,"y":0,"dir":"V"},
  {"name":"DESTROYER","x":3,"y":9,"dir":"H"}
]}}
```

(Ví dụ chia nhiều dòng cho dễ đọc, **trên dây phải là một dòng**.)

- Thành công: `ACK`. Lỗi: `INVALID_SHIPS` (kèm `message` nói rõ lý do, ví dụ tàu nào ra ngoài bảng).
- **Được gửi lại** nhiều lần khi còn ở `PLACING` để đổi vị trí; bản gửi sau thay bản trước. Sau khi `READY` thì không đổi được.

#### `READY` (C→S)

```json
{"type":"READY","seq":5,"data":{}}
```

Thành công: `ACK`. Chưa `PLACE_SHIPS` hợp lệ mà gửi `READY` → `NOT_PLACED`.

#### `GAME_START` (S→C)

```json
{"type":"GAME_START","seq":4,"data":{}}
```

Gửi cho **cả hai** khi cả hai đã `READY`. Ai bắn trước đã có trong `MATCH_FOUND.you_first`.

#### `FIRE` (C→S)

```json
{"type":"FIRE","seq":6,"data":{"x":3,"y":7}}
```

Lỗi (đều **không mất lượt**): `NOT_YOUR_TURN`, `OUT_OF_BOUNDS`, `ALREADY_FIRED`, `INVALID_PARAMS`, `BAD_STATE`.

#### `FIRE_RESULT` (S→C, gửi cho người bắn)

```json
{"type":"FIRE_RESULT","seq":10,"ref":6,"data":{"x":3,"y":7,"result":"SUNK","sunk_ship":"DESTROYER","sunk_cells":[{"x":3,"y":7},{"x":3,"y":8}],"next_turn":"you","game_over":false}}
```

| Trường | Ý nghĩa |
|---|---|
| `x`, `y` | Ô vừa bắn |
| `result` | `MISS`, `HIT`, `SUNK` |
| `sunk_ship` | Tên tàu vừa chìm, nếu `result` khác `SUNK` thì `null` |
| `sunk_cells` | Danh sách ô của tàu vừa chìm, nếu không chìm thì `[]` |
| `next_turn` | `"you"` hoặc `"opponent"` tính **theo người nhận message**. Khi `game_over` là `true` thì `null` |
| `game_over` | `true` nếu đây là phát bắn kết thúc ván |

Quy tắc đổi lượt (`HIT_KEEPS_TURN = True`): `HIT` hoặc `SUNK` thì người bắn **bắn tiếp**; `MISS` thì đổi lượt.

#### `OPPONENT_FIRED` (S→C, gửi cho người bị bắn)

Cùng cấu trúc `data` với `FIRE_RESULT`, nhưng **không có `ref`**, và `next_turn` tính theo người nhận:

```json
{"type":"OPPONENT_FIRED","seq":11,"data":{"x":3,"y":7,"result":"SUNK","sunk_ship":"DESTROYER","sunk_cells":[{"x":3,"y":7},{"x":3,"y":8}],"next_turn":"opponent","game_over":false}}
```

Client dùng message này để đánh dấu `HIT`/`MISS` lên **bảng của mình**.

#### `GAME_OVER` (S→C, gửi cho cả hai)

```json
{"type":"GAME_OVER","seq":12,"data":{"result":"WIN","reason":"ALL_SHIPS_SUNK","total_shots":47}}
```

| Trường | Giá trị |
|---|---|
| `result` | `"WIN"` hoặc `"LOSE"`, tính theo người nhận |
| `reason` | `ALL_SHIPS_SUNK` (hết tàu), `SURRENDER` (một bên đầu hàng), `DISCONNECT` (một bên mất kết nối) |
| `total_shots` | Tổng số phát bắn của cả hai bên |

**Thứ tự gửi khi ván kết thúc do phát bắn cuối:** người bắn nhận `FIRE_RESULT` rồi `GAME_OVER`; người bị bắn nhận `OPPONENT_FIRED` rồi `GAME_OVER`. Sau `GAME_OVER` cả hai về `AUTHED`.

#### `SURRENDER` (C→S)

```json
{"type":"SURRENDER","seq":8,"data":{}}
```

- Ở `PLAYING`: người gửi **thua**. Cả hai nhận `GAME_OVER` (`reason: "SURRENDER"`).
- Ở `PLACING` hoặc `READY` (chưa bắt đầu): coi như **rời trận**. Đối thủ nhận `MATCH_CANCELLED`, người gửi nhận `ACK`, cả hai về `AUTHED`. Không tính thắng thua.

#### `MATCH_CANCELLED` (S→C)

```json
{"type":"MATCH_CANCELLED","seq":9,"data":{"reason":"OPPONENT_LEFT"}}
```

Gửi cho người còn lại khi đối thủ rời, đầu hàng hoặc mất kết nối **trước khi** `GAME_START`. Người nhận về `AUTHED`.

#### `GET_STATS` (C→S) và `STATS` (S→C)

```json
{"type":"GET_STATS","seq":9,"data":{}}
```
```json
{"type":"STATS","seq":14,"ref":9,"data":{"played":12,"wins":7,"losses":5}}
```

#### `GET_HISTORY` (C→S) và `HISTORY` (S→C)

```json
{"type":"GET_HISTORY","seq":10,"data":{"limit":20}}
```
```json
{"type":"HISTORY","seq":15,"ref":10,"data":{"matches":[
  {"match_id":31,"opponent":"nam02","result":"WIN","total_shots":47,"started_at":"2026-11-16T09:30:00Z","ended_at":"2026-11-16T09:41:12Z"}
]}}
```

`limit` không bắt buộc (mặc định 20, tối đa 50). Sắp xếp mới nhất trước.

#### `PING` (C→S) và `PONG` (S→C)

```json
{"type":"PING","seq":11,"data":{}}
{"type":"PONG","seq":16,"ref":11,"data":{}}
```

#### `ACK` (S→C)

```json
{"type":"ACK","seq":3,"ref":4,"data":{}}
```

Dùng cho lệnh thành công mà không có dữ liệu trả về: `REGISTER`, `FIND_MATCH`, `PLACE_SHIPS`, `READY`, và `SURRENDER` khi ở `PLACING`/`READY`.

#### `ERROR` (S→C)

```json
{"type":"ERROR","seq":5,"ref":6,"data":{"code":"NOT_YOUR_TURN","message":"Chưa đến lượt của bạn"}}
```

- `code` dùng cho **code xử lý**, `message` là câu tiếng Việt cho người đọc, client **không** so sánh `message`.
- `ref` có nếu server đọc được `seq` của message gây lỗi, không đọc được (JSON hỏng) thì bỏ `ref`.
- Server **không đóng kết nối** khi gửi `ERROR`, trừ `LINE_TOO_LONG`.

## 6. Mã lỗi

| `code` | Khi nào |
|---|---|
| `BAD_JSON` | Dòng nhận được không phải JSON hợp lệ |
| `UNKNOWN_TYPE` | `type` không có trong giao thức |
| `INVALID_PARAMS` | Thiếu trường, sai kiểu dữ liệu, giá trị không hợp lệ |
| `LINE_TOO_LONG` | Dòng vượt 64 KB (server đóng kết nối) |
| `UNSUPPORTED_VERSION` | `version` không khớp |
| `BAD_STATE` | Gửi message không hợp lệ ở trạng thái hiện tại |
| `AUTH_FAILED` | Sai tên đăng nhập hoặc mật khẩu |
| `ALREADY_LOGGED_IN` | Tài khoản đang đăng nhập ở nơi khác |
| `USERNAME_TAKEN` | Tên đã tồn tại khi đăng ký |
| `INVALID_USERNAME` | Tên không đúng quy tắc (mục 5.2) |
| `WEAK_PASSWORD` | Mật khẩu không đúng quy tắc (mục 5.2) |
| `INVALID_SHIPS` | Vị trí tàu không hợp lệ (mục 3.3) |
| `NOT_PLACED` | `READY` khi chưa đặt tàu hợp lệ |
| `NOT_YOUR_TURN` | `FIRE` khi chưa đến lượt |
| `OUT_OF_BOUNDS` | Tọa độ bắn ngoài bảng 0–9 |
| `ALREADY_FIRED` | Ô này đã bắn rồi |
| `SERVER_ERROR` | Lỗi nội bộ server (server phải log đầy đủ và **không được sập**) |

## 7. Luồng một ván

```
Client A                    Server                    Client B
   │── LOGIN ────────────────▶│                          │
   │◀─ LOGIN_OK ──────────────│◀──────────── LOGIN ──────│
   │── FIND_MATCH ───────────▶│◀───────── FIND_MATCH ────│
   │◀─ ACK                    │                    ACK ─▶│
   │◀─ MATCH_FOUND (you_first=true)   MATCH_FOUND (you_first=false) ─▶│
   │── PLACE_SHIPS ──────────▶│                          │
   │◀─ ACK                    │◀──────── PLACE_SHIPS ────│
   │── READY ────────────────▶│                    ACK ─▶│
   │◀─ ACK                    │◀─────────────── READY ───│
   │◀─ GAME_START ────────────│────────── GAME_START ───▶│
   │── FIRE(3,7) ────────────▶│                          │
   │◀─ FIRE_RESULT (HIT, next_turn=you)                  │
   │                          │── OPPONENT_FIRED (HIT, next_turn=opponent) ─▶│
   │── FIRE(4,7) ────────────▶│                          │
   │◀─ FIRE_RESULT (MISS, next_turn=opponent)            │
   │                          │── OPPONENT_FIRED (MISS, next_turn=you) ─────▶│
   │                          │◀─────────────── FIRE ────│
   │          ...             │          ...             │
   │◀─ FIRE_RESULT (SUNK, game_over=true)                │
   │◀─ GAME_OVER (WIN)        │── OPPONENT_FIRED (game_over=true) ──▶│
   │                          │── GAME_OVER (LOSE) ─────▶│
```

## 8. Quy tắc cho server

### 8.1 Gọi logic của Khôi

Server gọi `GameSession` và **tự dịch** kết quả sang từng người nhận:

| Logic trả về (hợp đồng Khôi→Kiên) | Server dịch thành |
|---|---|
| `result` | `result` (`MISS`/`HIT`/`SUNK`) |
| `sunk_cells` | `sunk_cells` (đổi sang `[{x,y},...]`) và `sunk_ship` (tên tàu) |
| `next_turn` (người chơi nào) | `"you"`/`"opponent"` **riêng cho từng người nhận** |
| `game_over`, `winner` | `game_over` và `GAME_OVER.result` (`WIN`/`LOSE`) cho từng người |

> Đề nghị Khôi trả thêm **tên tàu bị chìm** (`sunk_ship`) trong kết quả `fire()`.

### 8.2 Kiểm tra và an toàn

- **Không tin client.** Luôn kiểm tra: trạng thái, lượt, kiểu dữ liệu, phạm vi tọa độ, tên tàu.
- Mọi lỗi dữ liệu từ client chỉ sinh `ERROR`, **không bao giờ làm server sập hay ném exception ra ngoài thread**.
- Không gửi vị trí tàu chưa chìm của ai cho đối thủ.
- Không ghi mật khẩu vào log.
- Mật khẩu đi qua TCP **không mã hóa** (chấp nhận trong phạm vi môn học, ghi vào báo cáo).

### 8.3 Đồng thời

- Mỗi `GameSession` dùng một `Lock` riêng. Hai thao tác cùng lúc trên một ván (ví dụ `FIRE` và `SURRENDER`) được xử lý lần lượt, **thao tác nào vào trước thì được tính**.
- Hàng đợi ghép trận dùng một `Lock` riêng.
- Gửi dữ liệu cho một socket phải được bảo vệ khỏi hai thread ghi xen kẽ.

### 8.4 Khi nào gọi DB (Duy)

| Sự kiện | Gọi |
|---|---|
| `REGISTER` hợp lệ | `register(u, p)` |
| `LOGIN` | `login(u, p)` |
| `GAME_START` | lưu ván mới (bắt đầu) |
| `GAME_OVER` | cập nhật kết quả, `total_shots`, `ended_at` |
| `GET_STATS` / `GET_HISTORY` | `get_stats(user_id)` / `get_history(user_id)` |

`MATCH_CANCELLED` (trước `GAME_START`) **không** lưu ván.

### 8.5 Mất kết nối

| Đang ở | Điều xảy ra |
|---|---|
| `CONNECTED`, `AUTHED` | Dọn kết nối |
| `IN_QUEUE` | Xóa khỏi hàng đợi |
| `PLACING`, `READY` | Đối thủ nhận `MATCH_CANCELLED` |
| `PLAYING` | Đối thủ nhận `GAME_OVER` (`WIN`, `reason: "DISCONNECT"`), lưu DB |

Phiên bản này **không hỗ trợ kết nối lại** giữa ván.

## 9. Các quyết định đã chốt (v1)

Các điểm dưới đây từng để ngỏ ở bản nháp, nay cả nhóm đã thống nhất theo cột **Quyết định**.

| # | Vấn đề | Quyết định | Trạng thái |
|---|---|---|---|
| 1 | Ai bắn trước | Server chọn ngẫu nhiên, gửi trong `MATCH_FOUND.you_first` | ☑ Đã chốt |
| 2 | Sau khi `READY` có đổi lại vị trí tàu không | Không. Trước `READY` thì được gửi lại `PLACE_SHIPS` | ☑ Đã chốt |
| 3 | Bắn ô đã bắn / ngoài bảng / sai lượt có mất lượt không | **Không mất lượt**, chỉ trả `ERROR` | ☑ Đã chốt |
| 4 | Giới hạn thời gian mỗi lượt | **Không có** (xem lưu ý bên dưới) | ☑ Đã chốt |
| 5 | Một tài khoản đăng nhập hai nơi | Từ chối bằng `ALREADY_LOGGED_IN` | ☑ Đã chốt |
| 6 | Thêm `ACK` và `MATCH_CANCELLED` | **Có**, vì lộ trình chưa có cách báo "lệnh thành công" và "trận hủy trước khi bắt đầu" | ☑ Đã chốt |
| 7 | Hủy tìm trận khi đang `IN_QUEUE` | Không làm trong v1 (thêm sau nếu kịp) | ☑ Ngoài phạm vi v1 |
| 8 | Khi hết ván có lộ vị trí tàu còn lại của đối thủ không | Không | ☑ Đã chốt |
| 9 | Chơi lại với cùng đối thủ (rematch) | Không làm trong v1, hết ván về sảnh | ☑ Ngoài phạm vi v1 |
| 10 | Quy tắc tên đăng nhập, mật khẩu | 3–20 ký tự `[A-Za-z0-9_]`, mật khẩu 6–64 ký tự | ☑ Đã chốt |

**Hạn chế đã biết (điểm 4):** vì không có giới hạn thời gian mỗi lượt, nếu một người bỏ đi mà không thoát thì ván sẽ treo (server chỉ phát hiện được khi mất kết nối). Nếu thấy ảnh hưởng khi demo thì cân nhắc thêm timeout mỗi lượt ở bản sau.


## Lịch sử thay đổi

| Phiên bản | Ngày | Người sửa | Nội dung |
|---|---|---|---|
| 0.1 (nháp) | | | Bản nháp đầu tiên |
| 1.0 | 2026-10-20 | Bảo Anh | Nhóm chốt v1: ghi nhận các quyết định ở mục 9, bỏ trạng thái nháp |
