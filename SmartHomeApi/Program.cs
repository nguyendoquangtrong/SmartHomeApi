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

// 1. Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Cấu hình JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
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

// 4. Định tuyến Middleware
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DeviceHub>("/deviceHub");

// 5. Khởi động
app.Run();