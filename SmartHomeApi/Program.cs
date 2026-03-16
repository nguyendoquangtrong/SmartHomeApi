using SmartHomeApi.Hubs;
using SmartHomeApi.Services;
using SmartHomeApi.Services.Interface;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký MqttService như một Background Service (chạy ngầm)
builder.Services.AddHostedService<MqttService>();

// Đồng thời đăng ký nó dưới dạng Singleton (chỉ 1 bản thể) để các API Controller có thể gọi được hàm Publish
builder.Services.AddSingleton<IMqttService>(sp =>
    sp.GetServices<IHostedService>().OfType<MqttService>().First());
builder.Services.AddSignalR();
builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

var app = builder.Build();

app.MapControllers();
app.MapHub<DeviceHub>("/deviceHub");
app.Run();