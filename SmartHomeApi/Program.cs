using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartHomeApi.Data;
using SmartHomeApi.Hubs;
using SmartHomeApi.Services;
using SmartHomeApi.Services.Interface;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// [MỚI] Cấu hình CORS để App/Web gọi được API không bị chặn
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 1. Cấu hình Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Cấu hình JWT Authentication
// Lấy Key từ cấu hình, nếu quên set trên Railway thì dùng Key mặc định để không bị Crash 500
var jwtKey = builder.Configuration["Jwt:Key"] ?? "DayLaMotChuoiKhoaBaoMatRatDaiVaKhoDoan123!@#";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SmartHomeIssuer";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SmartHomeAudience";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // Hỗ trợ Token cho SignalR (WebSockets)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/deviceHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// 3. Đăng ký các dịch vụ hệ thống
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHostedService<MqttService>();
builder.Services.AddSingleton<IMqttService>(sp =>
    sp.GetServices<IHostedService>().OfType<MqttService>().First());

var app = builder.Build();

// ====== TỰ ĐỘNG TẠO BẢNG DATABASE (Khắc phục triệt để lỗi 500) ======
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<SmartHomeApi.Data.AppDbContext>();
        context.Database.Migrate(); // Tự động chạy tạo bảng khi khởi động
        Console.WriteLine("Đã đồng bộ cấu trúc Database thành công!");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Lỗi khi đồng bộ Database: " + ex.Message);
    }
}
// ====================================================================

// 4. Định tuyến Middleware
app.UseCors(); // [MỚI] Kích hoạt CORS (Phải nằm trước Auth)
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DeviceHub>("/deviceHub");

// 5. Khởi động
app.Run();