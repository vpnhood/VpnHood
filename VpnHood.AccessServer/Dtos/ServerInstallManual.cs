using System.Text.Json;

namespace VpnHood.AccessServer.Dtos;

public class ServerInstallManual
{
    public string AppSettingsJson { get; }
    public ServerInstallAppSettings AppSettings { get; }
    public string LinuxCommand { get; }
        
    public ServerInstallManual(ServerInstallAppSettings appSettings, string linuxCommand)
    {
        AppSettings = appSettings;
        LinuxCommand = linuxCommand;
        AppSettingsJson = JsonSerializer.Serialize(appSettings, new JsonSerializerOptions{WriteIndented = true});
    }
}