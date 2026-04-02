using System.ComponentModel.DataAnnotations;

namespace SmartHomeApi.Models;

public class User
{
    [Key] public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    
    public string PushToken { get; set; } = string.Empty;
}