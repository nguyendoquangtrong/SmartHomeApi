using System.ComponentModel.DataAnnotations;

namespace SmartHomeApi.Models;

public class Device
{
    [Key] public string MacAddress { get; set; } = string.Empty;
    public string DeviceName { get; set; } = "Thiết bị mới"; // Tên thiết bị
    public int OwnerId { get; set; } = 0; // 0 nghĩa là chưa có ai sở hữu, >0 là ID của tài khoản gốc
    
    // Các thông số trạng thái (Gộp từ DeviceStatus cũ)
    public string Status { get; set; } = "OFFLINE";
    public int Speed { get; set; } = 0;
    public string IpAddress { get; set; } = "Unknown";
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}