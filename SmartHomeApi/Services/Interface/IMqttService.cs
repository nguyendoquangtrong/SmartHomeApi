namespace SmartHomeApi.Services.Interface
{
    public interface IMqttService
    {
        Task PublishCommandAsync(string macAddress, string action, int value = 0);
    }
}
