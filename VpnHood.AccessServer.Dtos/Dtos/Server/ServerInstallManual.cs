using System.Text.Json;

namespace VpnHood.AccessServer.Dtos;

public class ServerInstallManual
{
    public string AppSettingsJson { get; }
    public ServerInstallAppSettings AppSettings { get; }
    public required string LinuxCommand { get; init; }
    public required string WindowsCommand { get; init; }

    public ServerInstallManual(ServerInstallAppSettings appSettings)
    {
        AppSettings = appSettings;
        AppSettingsJson = JsonSerializer.Serialize(appSettings, new JsonSerializerOptions { WriteIndented = true });
    }
}