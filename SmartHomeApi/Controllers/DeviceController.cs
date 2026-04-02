using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartHomeApi.Data;
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

    public DeviceController(IMqttService mqttService, AppDbContext context)
    {
        _mqttService = mqttService;
        _context = context;
    }

    // ==========================================
    // 1. API THÊM THIẾT BỊ MỚI (ADD DEVICE)
    // ==========================================
    [HttpPost("add")]
    public IActionResult AddDevice([FromBody] AddDeviceRequest request)
    {
        // Lấy ID người dùng từ Token
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString)) return Unauthorized();
        var userId = int.Parse(userIdString);

        if (string.IsNullOrEmpty(request.MacAddress)) 
            return BadRequest(new { message = "Mã thiết bị (MacAddress) không được để trống!" });

        // Tìm thiết bị trong Database
        var device = _context.Devices.FirstOrDefault(d => d.MacAddress == request.MacAddress);
        
        if (device == null)
        {
            // Mạch chưa từng cắm điện (chưa ping lên MQTT), tạo sẵn dữ liệu
            _context.Devices.Add(new Device { 
                MacAddress = request.MacAddress, 
                DeviceName = string.IsNullOrEmpty(request.DeviceName) ? "Thiết bị mới" : request.DeviceName,
                OwnerId = userId 
            });
        }
        else
        {
            // Mạch đã cắm điện và ping lên MQTT
            if (device.OwnerId == 0)
            {
                device.OwnerId = userId; // Gán quyền sở hữu
                if (!string.IsNullOrEmpty(request.DeviceName))
                    device.DeviceName = request.DeviceName;
            }
            else if (device.OwnerId == userId)
            {
                return Ok(new { message = "Thiết bị này đã nằm trong danh sách của bạn rồi!" });
            }
            else
            {
                return BadRequest(new { message = "Thiết bị này đã thuộc về tài khoản khác!" });
            }
        }

        _context.SaveChanges();
        return Ok(new { message = $"Đã thêm thiết bị {request.MacAddress} thành công!" });
    }

    // ==========================================
    // 2. API ĐIỀU KHIỂN THIẾT BỊ
    // ==========================================
    [HttpPost("control")]
    public async Task<IActionResult> ControlDevice([FromBody] DeviceCommandRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Kiểm tra quyền Chủ hoặc quyền được Chia sẻ
        var isOwner = _context.Devices.Any(d => d.MacAddress == request.MacAddress && d.OwnerId == userId);
        var isShared = _context.DeviceShares.Any(s => s.MacAddress == request.MacAddress && s.SharedWithUserId == userId);

        if (!isOwner && !isShared) 
            return StatusCode(403, new { message = "Bạn không có quyền điều khiển thiết bị này!" });

        await _mqttService.PublishCommandAsync(request.MacAddress, request.Action, request.Value);
        return Ok(new { message = $"Đã gửi lệnh tới {request.MacAddress}" });
    }

    // ==========================================
    // 3. API CHIA SẺ THIẾT BỊ CHO NGƯỜI KHÁC
    // ==========================================
    [HttpPost("share")]
    public IActionResult ShareDevice([FromBody] ShareRequest request)
    {
        var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        
        var isOwner = _context.Devices.Any(d => d.MacAddress == request.MacAddress && d.OwnerId == ownerId);
        if (!isOwner) return StatusCode(403, new { message = "Chỉ TÀI KHOẢN GỐC mới được quyền chia sẻ!" });

        var targetUser = _context.Users.FirstOrDefault(u => u.Username == request.TargetUsername);
        if (targetUser == null) return NotFound(new { message = "Không tìm thấy tài khoản người nhận!" });

        if (_context.DeviceShares.Any(s => s.MacAddress == request.MacAddress && s.SharedWithUserId == targetUser.Id))
            return BadRequest(new { message = "Thiết bị này đã được chia sẻ cho người này rồi!" });

        _context.DeviceShares.Add(new DeviceShare { MacAddress = request.MacAddress, SharedWithUserId = targetUser.Id });
        _context.SaveChanges();

        return Ok(new { message = $"Đã chia sẻ thiết bị thành công cho {request.TargetUsername}!" });
    }

    // ==========================================
    // 4. API LẤY DANH SÁCH THIẾT BỊ CỦA TÔI
    // ==========================================
    [HttpGet("my-devices")]
    public IActionResult GetMyDevices()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Lấy cả máy mình làm chủ và máy được người khác chia sẻ
        var ownedDevices = _context.Devices.Where(d => d.OwnerId == userId).Select(d => d.MacAddress).ToList();
        var sharedDevices = _context.DeviceShares.Where(s => s.SharedWithUserId == userId).Select(s => s.MacAddress).ToList();
        var allMyMacs = ownedDevices.Concat(sharedDevices).Distinct().ToList();

        if (!allMyMacs.Any()) return Ok(new { message = "Bạn chưa có thiết bị nào." });

        var myDevicesStatus = MqttService.DeviceCache.Values
            .Where(device => allMyMacs.Contains(device.MacAddress))
            .ToList();

        return Ok(myDevicesStatus);
    }

    // Dùng để test (Lấy mọi thiết bị đang kết nối)
    [AllowAnonymous]
    [HttpGet("status-all")]
    public IActionResult GetAllStatus()
    {
        return Ok(MqttService.DeviceCache.Values.ToList());
    }
}

// ==========================================
// CÁC MODELS NHẬN DỮ LIỆU TỪ CLIENT
// ==========================================
public class AddDeviceRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string? DeviceName { get; set; } 
}

public class ShareRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string TargetUsername { get; set; } = string.Empty;
}