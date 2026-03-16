using System.ComponentModel.DataAnnotations;

namespace SmartHomeApi.Models;

public class UserDevice
{
    [Key] public int Id { get; set; }
    public int UserId { get; set; }
    public string MacAddress { get; set; } = string.Empty;
}