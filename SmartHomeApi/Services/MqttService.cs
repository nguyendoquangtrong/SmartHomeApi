using Microsoft.AspNetCore.SignalR;
using MQTTnet;
using MQTTnet.Client;
using SmartHomeApi.Hubs;
using SmartHomeApi.Models;
using SmartHomeApi.Services.Interface;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace SmartHomeApi.Services
{
    public class MqttService : IHostedService, IMqttService
    {
        private IMqttClient _mqttClient;
        private MqttClientOptions _mqttOptions;
        private readonly IHubContext<DeviceHub> _hubContext;

        public static ConcurrentDictionary<string, DeviceStatus> DeviceCache = new();

        public MqttService(IHubContext<DeviceHub> hubContext)
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();
            _hubContext = hubContext;


            // Cấu hình trỏ tới MQTT Broker của bạn (Mosquitto)
            _mqttOptions = new MqttClientOptionsBuilder()
                .WithClientId("CSharp_Backend_Server")
                .WithTcpServer("127.0.0.1", 1883) // Vì Server C# và Mosquitto đang chạy cùng 1 máy tính
                .WithCleanSession()
                .Build();

            // Xử lý sự kiện khi nhận được tin nhắn báo cáo từ mạch Pico
            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = e.ApplicationMessage.ConvertPayloadToString() ?? "{}";

                var parts = topic.Split('/');
                if (parts.Length >= 2 && topic.EndsWith("/status"))
                {
                    string mac = parts[1];

                    // Parse JSON an toàn
                    using var jsonDoc = JsonDocument.Parse(payload);
                    var root = jsonDoc.RootElement;

                    var statusEntry = DeviceCache.GetOrAdd(mac, new DeviceStatus { MacAddress = mac });

                    if (root.TryGetProperty("status", out var s)) statusEntry.Status = s.GetString() ?? "UNKNOWN";
                    if (root.TryGetProperty("ip", out var i)) statusEntry.IpAddress = i.GetString() ?? "0.0.0.0";

                    // Đọc thêm giá trị speed nếu Pico gửi lên
                    if (root.TryGetProperty("speed", out var sp)) statusEntry.Speed = sp.GetInt32();

                    statusEntry.LastUpdate = DateTime.Now;

                    await _hubContext.Clients.All.SendAsync("ReceiveDeviceStatus", statusEntry);
                }
            };
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
            Console.WriteLine("=> C# Backend đã kết nối thành công tới MQTT Broker!");

            // Đăng ký lắng nghe TẤT CẢ các thiết bị báo cáo trạng thái lên
            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic("device/+/status").Build());
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build(), cancellationToken);
        }

        // Hàm này sẽ được gọi từ các Controller (khi người dùng bấm nút trên App)
        public async Task PublishCommandAsync(string macAddress, string action, int value = 0)
        {
            if (!_mqttClient.IsConnected) return;

            string topic = $"device/{macAddress}/command";
            // Gửi cả action và value xuống Pico
            string payload = JsonSerializer.Serialize(new
            {
                action = action,
                value = value
            });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .Build();

            await _mqttClient.PublishAsync(message);
        }
    }
}
