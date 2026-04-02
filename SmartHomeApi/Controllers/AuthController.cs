using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SmartHomeApi.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SmartHomeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var user = _context.Users.FirstOrDefault(u => u.Username == request.Username && u.Password == request.Password);
        if (user == null) return Unauthorized(new { message = "Sai tài khoản hoặc mật khẩu" });

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var token = new JwtSecurityToken(_config["Jwt:Issuer"], _config["Jwt:Audience"],
            claims, expires: DateTime.Now.AddDays(7), signingCredentials: credentials);

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        if (_context.Users.Any(u => u.Username == request.Username))
            return BadRequest(new { message = "Tài khoản này đã tồn tại!" });

        var newUser = new Models.User { Username = request.Username, Password = request.Password };
        _context.Users.Add(newUser);
        _context.SaveChanges();

        // Xử lý xác nhận chủ sở hữu thiết bị
        if (!string.IsNullOrEmpty(request.MacAddress))
        {
            var device = _context.Devices.FirstOrDefault(d => d.MacAddress == request.MacAddress);
            if (device == null)
            {
                // Mạch chưa từng ping lên MQTT, tạo sẵn chờ nó ping
                _context.Devices.Add(new Models.Device { MacAddress = request.MacAddress, OwnerId = newUser.Id });
            }
            else
            {
                // Mạch đã ping lên MQTT, kiểm tra xem đã có chủ chưa
                if (device.OwnerId == 0) device.OwnerId = newUser.Id;
                else
                    return Ok(new
                    {
                        message = "Đăng ký thành công, nhưng thiết bị này đã thuộc về người khác!", userId = newUser.Id
                    });
            }

            _context.SaveChanges();
        }

        return Ok(new { message = "Đăng ký thành công và đã gán thiết bị!", userId = newUser.Id });
    }
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet("profile")]
    public IActionResult GetProfile()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString)) return Unauthorized();
        
        var userId = int.Parse(userIdString);
        var user = _context.Users.Find(userId);
        
        if (user == null) return NotFound();
        
        return Ok(new { 
            id = user.Id, 
            username = user.Username,
            role = "User" 
        });
    }
}

// Thêm class này ở cuối file (dưới LoginRequest)
public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty; // Mạch Pico bạn muốn sở hữu
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}