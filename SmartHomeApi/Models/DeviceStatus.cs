namespace SmartHomeApi.Models;

public class DeviceStatus
{
    public string MacAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "OFFLINE";
    public int Speed { get; set; } = 0; // Thêm trường này
    public string IpAddress { get; set; } = "Unknown";
    public DateTime LastUpdate { get; set; } = DateTime.Now;
}