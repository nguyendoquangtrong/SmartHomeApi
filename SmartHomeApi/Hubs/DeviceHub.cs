using Microsoft.AspNetCore.SignalR;
using SmartHomeApi.Services;

namespace SmartHomeApi.Hubs;

public class DeviceHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var allDevices = MqttService.DeviceCache.Values.ToList();
        await Clients.Caller.SendAsync("ReceiveAllDevices", allDevices);
        await base.OnConnectedAsync();
    }
}