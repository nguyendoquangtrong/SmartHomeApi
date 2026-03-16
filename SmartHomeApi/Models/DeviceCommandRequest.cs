namespace SmartHomeApi.Models;

public class DeviceCommandRequest
{
    public string MacAddress { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int Value { get; set; }
}