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
    private readonly IHubContext<DeviceHub> _hubContext; // Dùng để đẩy thông báo Real-time

    public DeviceController(IMqttService mqttService, AppDbContext context, IHubContext<DeviceHub> hubContext)
    {
        _mqttService = mqttService;
        _context = context;
        _hubContext = hubContext;
    }

    // ... (Giữ nguyên API AddDevice và ShareDevice ở đây) ...

    [HttpPost("control")]
    public async Task<IActionResult> ControlDevice([FromBody] DeviceCommandRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var username = User.FindFirstValue(ClaimTypes.Name)!; // Lấy tên người gọi lệnh

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
            TriggeredBy = username, // Ghi tên người dùng
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
                role = "OWNER", // Tag Sở hữu
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
                    role = "SHARED", // Tag Chia sẻ
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
    // LẤY LỊCH SỬ ĐIỀU CHỈNH
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