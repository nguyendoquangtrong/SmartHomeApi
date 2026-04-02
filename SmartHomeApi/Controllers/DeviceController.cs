using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SmartHomeApi.Data;
using SmartHomeApi.Hubs;
using SmartHomeApi.Models;
using SmartHomeApi.Services;
using SmartHomeApi.Services.Interface;
using System.Security.Claims;

namespace SmartHomeApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private readonly IMqttService _mqttService;
    private readonly AppDbContext _context;
    private readonly IHubContext<DeviceHub> _hubContext; 

    public DeviceController(IMqttService mqttService, AppDbContext context, IHubContext<DeviceHub> hubContext)
    {
        _mqttService = mqttService;
        _context = context;
        _hubContext = hubContext;
    }

    // ==========================================
    // 1. THÊM THIẾT BỊ MỚI (CÓ BẢO MẬT BẰNG PIN)
    // ==========================================
    [HttpPost("add")]
    public async Task<IActionResult> AddDevice([FromBody] AddDeviceRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var device = _context.Devices.FirstOrDefault(d => d.MacAddress == request.MacAddress);
        
        // Chặn 1: Mạch chưa từng được cắm điện (Server chưa biết nó là ai)
        if (device == null) 
            return NotFound(new { message = "Thiết bị chưa từng kết nối mạng. Hãy cắm điện thiết bị trước!" });

        // Chặn 2: Đã có người nhận chủ (Chặn bị cướp quyền)
        if (device.OwnerId != 0) 
            return BadRequest(new { message = "Thiết bị này đã có chủ sở hữu!" });

        // Chặn 3: NHẬP SAI MẬT KHẨU
        if (device.DevicePassword != request.DevicePassword)
            return BadRequest(new { message = "Mật khẩu thiết bị (PIN) không chính xác!" });

        // Vượt qua 3 ải -> Cho phép nhận chủ
        device.OwnerId = userId;
        device.DeviceName = request.DeviceName;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Ghép nối thiết bị thành công!" });
    }

    // ==========================================
    // 2. CHIA SẺ THIẾT BỊ
    // ==========================================
    [HttpPost("share")]
    public async Task<IActionResult> ShareDevice([FromBody] ShareDeviceRequest request)
    {
        var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Kiểm tra xem người gọi có phải là chủ sở hữu không
        var device = _context.Devices.FirstOrDefault(d => d.MacAddress == request.MacAddress && d.OwnerId == ownerId);
        if (device == null)
            return StatusCode(403, new { message = "Bạn không phải chủ sở hữu hoặc thiết bị không tồn tại!" });

        // Tìm tài khoản được chia sẻ
        var targetUser = _context.Users.FirstOrDefault(u => u.Username == request.SharedWithUsername);
        if (targetUser == null)
            return NotFound(new { message = "Không tìm thấy tên người dùng này trong hệ thống!" });

        if (targetUser.Id == ownerId)
            return BadRequest(new { message = "Không thể tự chia sẻ cho chính mình!" });

        // Kiểm tra xem đã chia sẻ cho người này chưa
        var existingShare = _context.DeviceShares.FirstOrDefault(s => s.MacAddress == request.MacAddress && s.SharedWithUserId == targetUser.Id);
        if (existingShare != null)
            return BadRequest(new { message = "Thiết bị này đã được chia sẻ cho người dùng này rồi!" });

        // Thêm quyền chia sẻ
        _context.DeviceShares.Add(new DeviceShare {
            MacAddress = request.MacAddress,
            SharedWithUserId = targetUser.Id
        });
        
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Đã chia sẻ thiết bị cho {request.SharedWithUsername} thành công!" });
    }

    // ==========================================
    // 3. ĐIỀU KHIỂN THIẾT BỊ
    // ==========================================
    [HttpPost("control")]
    public async Task<IActionResult> ControlDevice([FromBody] DeviceCommandRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var username = User.FindFirstValue(ClaimTypes.Name)!; 

        var device = _context.Devices.FirstOrDefault(d => d.MacAddress == request.MacAddress);
        if (device == null) return NotFound(new { message = "Không tìm thấy thiết bị!" });

        var isOwner = device.OwnerId == userId;
        var isShared = _context.DeviceShares.Any(s => s.MacAddress == request.MacAddress && s.SharedWithUserId == userId);

        if (!isOwner && !isShared) 
            return StatusCode(403, new { message = "Bạn không có quyền điều khiển!" });

        if (device.Status == "OFFLINE" || device.Status == "UNKNOWN")
            return BadRequest(new { message = "Thiết bị hiện đang mất kết nối!" });

        // Ghi lại Lịch sử
        var history = new DeviceHistory {
            MacAddress = request.MacAddress,
            Action = request.Action,
            Value = request.Value,
            TriggeredBy = username, 
            Timestamp = DateTime.UtcNow
        };
        _context.DeviceHistories.Add(history);
        await _context.SaveChangesAsync();

        // Gửi lệnh lên MQTT
        await _mqttService.PublishCommandAsync(request.MacAddress, request.Action, request.Value);

        // Bắn thông báo Real-time cho tất cả thiết bị đang mở App/Web
        var notifMsg = new {
            type = "USER_ACTION",
            message = $"Tài khoản '{username}' vừa chỉnh quạt '{device.DeviceName}' thành {request.Value}%",
            time = DateTime.UtcNow
        };
        await _hubContext.Clients.All.SendAsync("ReceiveNotification", notifMsg);

        return Ok(new { message = $"Đã gửi lệnh tới {request.MacAddress}" });
    }

    // ==========================================
    // 4. LẤY DANH SÁCH THIẾT BỊ
    // ==========================================
    [HttpGet("my-devices")]
    public IActionResult GetMyDevices()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var owned = _context.Devices.Where(d => d.OwnerId == userId).ToList();
        var sharedMacs = _context.DeviceShares.Where(s => s.SharedWithUserId == userId).Select(s => s.MacAddress).ToList();
        
        var result = new List<object>();

        // Thêm tag "Chủ sở hữu"
        foreach (var d in owned)
        {
            var cache = MqttService.DeviceCache.GetValueOrDefault(d.MacAddress);
            result.Add(new {
                macAddress = d.MacAddress,
                deviceName = d.DeviceName,
                role = "OWNER", 
                status = cache?.Status ?? d.Status,
                speed = cache?.Speed ?? d.Speed,
                ipAddress = cache?.IpAddress ?? d.IpAddress,
                lastUpdate = cache?.LastUpdate ?? d.LastUpdate
            });
        }

        // Thêm tag "Được chia sẻ"
        foreach (var mac in sharedMacs)
        {
            var d = _context.Devices.FirstOrDefault(x => x.MacAddress == mac);
            if (d != null)
            {
                var cache = MqttService.DeviceCache.GetValueOrDefault(d.MacAddress);
                result.Add(new {
                    macAddress = d.MacAddress,
                    deviceName = d.DeviceName,
                    role = "SHARED", 
                    status = cache?.Status ?? d.Status,
                    speed = cache?.Speed ?? d.Speed,
                    ipAddress = cache?.IpAddress ?? d.IpAddress,
                    lastUpdate = cache?.LastUpdate ?? d.LastUpdate
                });
            }
        }

        return Ok(result);
    }

    // ==========================================
    // 5. LẤY LỊCH SỬ ĐIỀU CHỈNH
    // ==========================================
    [HttpGet("{macAddress}/history")]
    public IActionResult GetHistory(string macAddress)
    {
        var history = _context.DeviceHistories
            .Where(h => h.MacAddress == macAddress)
            .OrderByDescending(h => h.Timestamp)
            .Take(50) // Lấy 50 hành động gần nhất
            .ToList();
            
        return Ok(history);
    }
}

// ==========================================
// CÁC CLASS ĐỊNH NGHĨA DỮ LIỆU ĐẦU VÀO
// ==========================================
public class AddDeviceRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DevicePassword { get; set; } = string.Empty; // Mã PIN bảo mật
}

public class ShareDeviceRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string SharedWithUsername { get; set; } = string.Empty; // Tên tài khoản người nhận
}