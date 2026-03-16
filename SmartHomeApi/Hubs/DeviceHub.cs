using Microsoft.AspNetCore.SignalR;
using SmartHomeApi.Services;

namespace SmartHomeApi.Hubs;

public class DeviceHub : Hub
{
    // Khi một Client (Postman/Web) vừa kết nối thành công
    public override async Task OnConnectedAsync()
    {
        var allDevices = SmartHomeApi.Services.MqttService.DeviceCache.Values.ToList();
        await Clients.Caller.SendAsync("ReceiveAllDevices", allDevices);
        await base.OnConnectedAsync();
    }
}