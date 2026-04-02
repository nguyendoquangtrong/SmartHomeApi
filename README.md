# 📚 Smart Home API Documentation

Tài liệu hướng dẫn tích hợp API cho ứng dụng di động (Mobile App) hoặc Web App quản lý thiết bị nhà thông minh.

## 🌐 Môi trường (Environments)

- **Base HTTP URL:** `https://smarthomeapi-production.up.railway.app/`
- **Base WebSocket URL:** `wss://smarthomeapi-production.up.railway.app`

## 🔒 Xác thực (Authentication)

Hầu hết các API đều yêu cầu xác thực bằng **JWT Token**.  
Bạn cần đính kèm token vào Header của các HTTP Request theo chuẩn:

1. 🧑‍💻 TÀI KHOẢN (AUTHENTICATION)
1.1. Đăng ký tài khoản
Endpoint: POST /api/auth/register
Yêu cầu xác thực (Auth): Không

Body (JSON):

{
  "username": "trongnguyen",
  "password": "password123",
  "macAddress": "pico_01"
}

Ghi chú: Truyền macAddress để hệ thống tự động khởi tạo và gán thiết bị ngay khi tạo tài khoản thành công.

Response (Thành công): 200 OK với thông báo đăng ký thành công.

1.2. Đăng nhập
Endpoint: POST /api/auth/login
Yêu cầu xác thực (Auth): Không

Body (JSON):

{
  "username": "trongnguyen",
  "password": "password123"
}

Response (Thành công): 200 OK trả về chuỗi token dùng để gọi các API khác.

1.3. Lấy thông tin cá nhân (Profile)
Endpoint: GET /api/auth/profile
Yêu cầu xác thực (Auth): Có (Bearer Token)

Response:

{
  "id": 1,
  "username": "trongnguyen",
  "role": "User"
}
2. 🎛️ QUẢN LÝ THIẾT BỊ (DEVICES)
2.1. Lấy danh sách thiết bị

Lấy toàn bộ thiết bị mà user đang sở hữu hoặc được người khác chia sẻ.

Endpoint: GET /api/device/my-devices
Yêu cầu xác thực (Auth): Có (Bearer Token)

Response:

[
  {
    "macAddress": "pico_01",
    "deviceName": "Quạt phòng ngủ",
    "role": "OWNER",
    "status": "RUNNING",
    "speed": 100,
    "ipAddress": "192.168.1.11",
    "lastUpdate": "2026-04-02T16:01:00Z"
  }
]

Ghi chú:

role có thể là "OWNER" (Chủ sở hữu) hoặc "SHARED" (Được chia sẻ).
status có thể là "RUNNING", "OFF", hoặc "OFFLINE".
2.2. Điều khiển thiết bị
Endpoint: POST /api/device/control
Yêu cầu xác thực (Auth): Có (Bearer Token)

Body (JSON):

{
  "macAddress": "pico_01",
  "action": "SET_SPEED",
  "value": 50
}

Ghi chú:

action có thể là "SET_SPEED" hoặc "OFF".
value nhận giá trị từ 0 đến 100.

Lưu ý: API sẽ trả về lỗi 400 Bad Request nếu thiết bị đang bị mất điện (OFFLINE) hoặc 403 Forbidden nếu người dùng không có quyền điều khiển.

2.3. Xem lịch sử hoạt động
Endpoint: GET /api/device/{macAddress}/history
Yêu cầu xác thực (Auth): Có (Bearer Token)

Response: Trả về mảng tối đa 50 hành động gần nhất của thiết bị.

[
  {
    "id": 5,
    "macAddress": "pico_01",
    "action": "MANUAL_ADJUST",
    "value": 0,
    "triggeredBy": "Điều chỉnh trực tiếp tại mạch",
    "timestamp": "2026-04-02T16:09:49Z"
  },
  {
    "id": 4,
    "macAddress": "pico_01",
    "action": "SET_SPEED",
    "value": 88,
    "triggeredBy": "trongnguyen",
    "timestamp": "2026-04-02T16:00:00Z"
  }
]
2.4. Thêm thiết bị mới (Add Device)

Dành cho trường hợp người dùng đã có tài khoản và muốn mua thêm quạt mới để thêm vào nhà.

Endpoint: POST /api/device/add
Yêu cầu xác thực (Auth): Có (Bearer Token)

Body (JSON):

{
  "macAddress": "pico_02",
  "deviceName": "Quạt phòng khách"
}
2.5. Chia sẻ thiết bị (Share Device)

Chia sẻ quyền điều khiển quạt cho một thành viên khác trong gia đình.

Endpoint: POST /api/device/share
Yêu cầu xác thực (Auth): Có (Bearer Token)

Body (JSON):

{
  "macAddress": "pico_01",
  "sharedWithUsername": "nguoithan123"
}
3. ⚡ KẾT NỐI REAL-TIME (SIGNALR / WEBSOCKET)

Để nhận được trạng thái quạt xoay trực tiếp và các thông báo cảnh báo mà không cần F5/Reload ứng dụng, app cần kết nối vào đường ống WebSocket.

URL kết nối:
wss://<link-railway-cua-ban>.up.railway.app/deviceHub?access_token=<Your_Token>
Giao thức: SignalR
(Bắt buộc gửi Handshake sau khi kết nối thành công)

Handshake Message (Khởi tạo):

{"protocol":"json","version":1}

Lưu ý: Bắt buộc có ký tự đặc biệt ASCII 30  ở cuối tin nhắn Handshake.

Các Event (Sự kiện) app cần lắng nghe
🟢 Event: ReceiveDeviceStatus

Phát ra mỗi khi quạt thay đổi tốc độ, bật/tắt, hoặc rớt mạng (kể cả do app chỉnh hay vặn tay trực tiếp).

Cấu trúc dữ liệu nhận được:

{
  "target": "ReceiveDeviceStatus",
  "arguments": [
    {
      "macAddress": "pico_01",
      "deviceName": "Quạt thông minh",
      "ownerId": 1,
      "status": "RUNNING",
      "speed": 88,
      "ipAddress": "192.168.1.11",
      "lastUpdate": "2026-04-02T16:01:02Z"
    }
  ]
}
🔴 Event: ReceiveNotification

Phát ra dưới dạng thông báo để hiển thị Toast/Popup/Snackbar trên điện thoại hoặc lưu vào danh sách chuông thông báo.

Cấu trúc dữ liệu nhận được:

{
  "target": "ReceiveNotification",
  "arguments": [
    {
      "type": "HARDWARE_ACTION",
      "message": "Cảnh báo: Quạt 'Quạt thông minh' vừa bị vặn tay để TẮT",
      "time": "2026-04-02T16:01:02Z"
    }
  ]
}

Ghi chú: type có thể là:

"HARDWARE_ACTION": thao tác vật lý trực tiếp tại mạch
"USER_ACTION": thao tác qua app của các thành viên
