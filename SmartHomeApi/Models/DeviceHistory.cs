using System.ComponentModel.DataAnnotations;

namespace SmartHomeApi.Models;

public class DeviceHistory
{
    [Key] public int Id { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // Ví dụ: SET_SPEED, MANUAL_ADJUST
    public int Value { get; set; }
    public string TriggeredBy { get; set; } = string.Empty; // Ai là người vặn (Username hoặc "Thiết bị")
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}