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

    [HttpPost("control")]
    public async Task<IActionResult> ControlDevice([FromBody] DeviceCommandRequest request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString)) return Unauthorized();
        var userId = int.Parse(userIdString);

        bool hasAccess = _context.UserDevices.Any(ud => ud.UserId == userId && ud.MacAddress == request.MacAddress);
        if (!hasAccess) return StatusCode(403, new { message = "Bạn không có quyền điều khiển thiết bị này!" });

        await _mqttService.PublishCommandAsync(request.MacAddress, request.Action, request.Value);
        return Ok(new { message = $"Đã gửi lệnh {request.Action} mức {request.Value} tới {request.MacAddress}" });
    }

    [HttpGet("my-devices")]
    public IActionResult GetMyDevices()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString)) return Unauthorized();
        var userId = int.Parse(userIdString);

        var myMacAddresses = _context.UserDevices
            .Where(ud => ud.UserId == userId)
            .Select(ud => ud.MacAddress)
            .ToList();

        if (!myMacAddresses.Any()) return Ok(new { message = "Bạn chưa có thiết bị nào." });

        var myDevicesStatus = MqttService.DeviceCache.Values
            .Where(device => myMacAddresses.Contains(device.MacAddress))
            .ToList();

        return Ok(myDevicesStatus);
    }

    [AllowAnonymous]
    [HttpGet("status-all")]
    public IActionResult GetAllStatus()
    {
        return Ok(MqttService.DeviceCache.Values.ToList());
    }
}