using Microsoft.AspNetCore.Mvc;
using SmartHomeApi.Services;
using SmartHomeApi.Services.Interface;

namespace SmartHomeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private readonly IMqttService _mqttService;

    // Tiêm (Inject) MqttService vào Controller
    public DeviceController(IMqttService mqttService)
    {
        _mqttService = mqttService;
    }

    // API để điều khiển thiết bị: POST /api/device/control
    [HttpPost("control")]
    public async Task<IActionResult> ControlDevice([FromBody] DeviceCommandRequest request)
    {
        // Chú ý: Ở Giai đoạn 4, chúng ta sẽ kiểm tra xem User có quyền điều khiển macAddress này không tại đây.
        // Tạm thời để test luồng, chúng ta cho phép gửi lệnh luôn.

        await _mqttService.PublishCommandAsync(request.MacAddress, request.Action, request.Value);
        return Ok(new { message = $"Đã gửi lệnh {request.Action} tới thiết bị {request.MacAddress}" });
    }

    [HttpGet("status-all")]
    public IActionResult GetAllStatus()
    {
        // Lấy tất cả dữ liệu từ Cache ra trả về cho Client
        var statuses = MqttService.DeviceCache.Values.ToList();
        return Ok(statuses);
    }

    [HttpGet("status/{macAddress}")]
    public IActionResult GetStatusByMac(string macAddress)
    {
        if (MqttService.DeviceCache.TryGetValue(macAddress, out var status))
        {
            return Ok(status);
        }
        return NotFound(new { message = "Không tìm thấy thiết bị hoặc thiết bị chưa báo cáo trạng thái." });
    }
}

// Cấu trúc cục dữ liệu Body mà App/Web sẽ gửi lên


public class DeviceCommandRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int Value { get; set; } // Thêm trường này để nhận tốc độ từ 0-100
}