using System.ComponentModel.DataAnnotations;

namespace SmartHomeApi.Models;

public class DeviceStatus
{
    [Key] public string MacAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "OFFLINE";
    public int Speed { get; set; } = 0;
    public string IpAddress { get; set; } = "Unknown";
    public DateTime LastUpdate { get; set; } = DateTime.Now;
}