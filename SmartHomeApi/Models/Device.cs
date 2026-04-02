using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization; // Thêm dòng này

namespace SmartHomeApi.Models;

public class Device
{
    [Key] public string MacAddress { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string Status { get; set; } = "UNKNOWN";
    public int Speed { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    
    [JsonIgnore]
    public string DevicePassword { get; set; } = string.Empty;
}