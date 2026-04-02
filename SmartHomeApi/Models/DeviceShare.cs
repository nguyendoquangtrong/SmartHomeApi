using System.ComponentModel.DataAnnotations;

namespace SmartHomeApi.Models;

public class DeviceShare
{
    [Key] public int Id { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public int SharedWithUserId { get; set; } // ID của người được cho phép dùng chung
}