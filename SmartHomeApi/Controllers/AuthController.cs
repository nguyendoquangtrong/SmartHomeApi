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

        var claims = new[] {
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
        // 1. Kiểm tra xem Username đã tồn tại chưa
        if (_context.Users.Any(u => u.Username == request.Username))
        {
            return BadRequest(new { message = "Tài khoản này đã tồn tại!" });
        }

        // 2. Tạo User mới
        var newUser = new Models.User 
        { 
            Username = request.Username, 
            Password = request.Password // Lưu ý: Thực tế phải dùng BCrypt để mã hóa mật khẩu
        };
        
        _context.Users.Add(newUser);
        _context.SaveChanges(); // Lưu vào DB để lấy ID

        // 3. Gán quyền sở hữu mạch Pico cho User này luôn
        var userDevice = new Models.UserDevice
        {
            UserId = newUser.Id,
            MacAddress = request.MacAddress
        };
        
        _context.UserDevices.Add(userDevice);
        _context.SaveChanges();

        return Ok(new { message = "Đăng ký thành công và đã gán thiết bị!", userId = newUser.Id });
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