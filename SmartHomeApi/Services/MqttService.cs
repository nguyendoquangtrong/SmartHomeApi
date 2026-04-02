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
                statusEntry.LastUpdate = DateTime.Now;

                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var deviceInDb = await dbContext.Devices.FindAsync(mac);
                    if (deviceInDb == null)
                    {
                        dbContext.Devices.Add(statusEntry); // Lúc này OwnerId mặc định = 0 (Chưa ai sở hữu)
                    }
                    else
                    {
                        deviceInDb.Status = statusEntry.Status;
                        deviceInDb.Speed = statusEntry.Speed;
                        deviceInDb.IpAddress = statusEntry.IpAddress;
                        deviceInDb.LastUpdate = statusEntry.LastUpdate;
                    }
                    await dbContext.SaveChangesAsync();
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
}