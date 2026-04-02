using Microsoft.AspNetCore.SignalR;
using MQTTnet;
using MQTTnet.Client;
using SmartHomeApi.Hubs;
using SmartHomeApi.Models;
using SmartHomeApi.Services.Interface;
using SmartHomeApi.Data;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SmartHomeApi.Services;

public class MqttService : IHostedService, IMqttService
{
    private IMqttClient _mqttClient;
    private MqttClientOptions _mqttOptions;
    private readonly IHubContext<DeviceHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;

    public static ConcurrentDictionary<string, Device> DeviceCache = new();
    public MqttService(IHubContext<DeviceHub> hubContext, IServiceScopeFactory scopeFactory)
    {
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;

        _mqttOptions = new MqttClientOptionsBuilder()
            .WithClientId("CSharp_Backend_Server_" + Guid.NewGuid().ToString())
            .WithTcpServer("4a6480af0e0d4d2f8f617239026d13f1.s1.eu.hivemq.cloud", 8883) 
            .WithCredentials("admin_smarthome", "MatKhauKho123") 
            .WithTls() 
            .WithCleanSession()
            .Build();

        _mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            string topic = e.ApplicationMessage.Topic;
            string payload = e.ApplicationMessage.ConvertPayloadToString() ?? "{}";

            var parts = topic.Split('/');
            if (parts.Length >= 2 && topic.EndsWith("/status"))
            {
                string mac = parts[1];
                using var jsonDoc = JsonDocument.Parse(payload);
                var root = jsonDoc.RootElement;

                var statusEntry = DeviceCache.GetOrAdd(mac, new Device { MacAddress = mac });

                if (root.TryGetProperty("status", out var s)) statusEntry.Status = s.GetString() ?? "UNKNOWN";
                if (root.TryGetProperty("ip", out var i)) statusEntry.IpAddress = i.GetString() ?? "0.0.0.0";
                if (root.TryGetProperty("speed", out var sp)) statusEntry.Speed = sp.GetInt32();

                // Đọc nhãn nguồn gốc của thao tác (từ Pico gửi lên)
                string source = "unknown";
                if (root.TryGetProperty("source", out var src)) source = src.GetString() ?? "unknown";

                // ĐỌC MẬT KHẨU GHÉP NỐI (DEVICE PIN) TỪ MẠCH PICO
                if (root.TryGetProperty("password", out var pwd)) 
                    statusEntry.DevicePassword = pwd.GetString() ?? "";

                statusEntry.LastUpdate = DateTime.UtcNow;

                // DÙNG TRY-CATCH ĐỂ BẢO VỆ LUỒNG SIGNALR KHÔNG BỊ CRASH
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var deviceInDb = await dbContext.Devices.FindAsync(mac);

                        if (deviceInDb == null)
                        {
                            // Mạch mới tinh -> Lưu vào DB kèm theo mật khẩu luôn
                            dbContext.Devices.Add(statusEntry);
                        }
                        else
                        {
                            // 0. CẬP NHẬT MẬT KHẨU NẾU MẠCH ĐỔI MẬT KHẨU MỚI
                            if (!string.IsNullOrEmpty(statusEntry.DevicePassword)) 
                            {
                                deviceInDb.DevicePassword = statusEntry.DevicePassword;
                            }

                            // 1. ĐỒNG BỘ DỮ LIỆU TỪ DB RA CACHE ĐỂ KHÔNG BỊ LỖI OWNER_ID = 0 VÀ LƯU LẠI MẬT KHẨU VÀO CACHE
                            statusEntry.OwnerId = deviceInDb.OwnerId;
                            statusEntry.DeviceName = deviceInDb.DeviceName;
                            statusEntry.DevicePassword = deviceInDb.DevicePassword;

                            // 2. KIỂM TRA LỊCH SỬ NẾU VẶN TAY VÀ BẮN PUSH NOTIFICATION
                            if (source == "manual" && deviceInDb.Speed != statusEntry.Speed)
                            {
                                string actionText = statusEntry.Speed == 0 
                                    ? "bị vặn tay để TẮT" 
                                    : $"bị vặn tay vật lý thành {statusEntry.Speed}%";

                                dbContext.DeviceHistories.Add(new DeviceHistory {
                                    MacAddress = mac,
                                    Action = "MANUAL_ADJUST",
                                    Value = statusEntry.Speed,
                                    TriggeredBy = "Điều chỉnh trực tiếp tại mạch", 
                                    Timestamp = DateTime.UtcNow
                                });

                                var notifMsg = new {
                                    type = "HARDWARE_ACTION",
                                    message = $"Cảnh báo: Quạt '{deviceInDb.DeviceName}' vừa {actionText}",
                                    time = DateTime.UtcNow
                                };
                                
                                // Gửi qua WebSocket (App đang mở)
                                await _hubContext.Clients.All.SendAsync("ReceiveNotification", notifMsg);

                                // ========================================================
                                // GỌI HÀM BẮN PUSH NOTIFICATION (APP ĐANG TẮT)
                                // ========================================================
                                var owner = await dbContext.Users.FindAsync(deviceInDb.OwnerId);
                                if (owner != null && !string.IsNullOrEmpty(owner.PushToken))
                                {
                                    await SendExpoPushNotification(
                                        owner.PushToken, 
                                        "Cảnh báo phần cứng 🛠️", 
                                        notifMsg.message
                                    );
                                }
                            }

                            // 3. CẬP NHẬT DB
                            deviceInDb.Status = statusEntry.Status;
                            deviceInDb.Speed = statusEntry.Speed;
                            deviceInDb.IpAddress = statusEntry.IpAddress;
                            deviceInDb.LastUpdate = statusEntry.LastUpdate;
                        }

                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LỖI DATABASE] Không thể cập nhật dữ liệu: {ex.Message}");
                }

                await _hubContext.Clients.All.SendAsync("ReceiveDeviceStatus", statusEntry);
            }
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
        Console.WriteLine("=> C# Backend đã kết nối thành công tới MQTT Broker!");
        await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic("device/+/status").Build());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build(), cancellationToken);
    }

    public async Task PublishCommandAsync(string macAddress, string action, int value = 0)
    {
        if (!_mqttClient.IsConnected) return;

        string topic = $"device/{macAddress}/command";
        string payload = JsonSerializer.Serialize(new { action = action, value = value });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        await _mqttClient.PublishAsync(message);
    }

    // HÀM BẮN THÔNG BÁO QUA EXPO PUSH SERVER
    private async Task SendExpoPushNotification(string expoPushToken, string title, string body)
    {
        if (string.IsNullOrEmpty(expoPushToken)) return;

        try
        {
            using var client = new HttpClient();
            var message = new
            {
                to = expoPushToken,
                sound = "default",
                title = title,
                body = body
            };

            await client.PostAsJsonAsync("https://exp.host/--/api/v2/push/send", message);
            Console.WriteLine($"[PUSH] Đã bắn thông báo tới: {expoPushToken}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PUSH ERROR] Lỗi bắn thông báo: {ex.Message}");
        }
    }
}