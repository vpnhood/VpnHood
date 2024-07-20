using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace VpnHood.AccessServer.Dtos.Servers;

public class ServerInstallManual(ServerInstallAppSettings appSettings)
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public string AppSettingsJson { get; } =
        JsonSerializer.Serialize(appSettings, new JsonSerializerOptions { WriteIndented = true });

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public ServerInstallAppSettings AppSettings { get; } = appSettings;

    public required string LinuxCommand { get; init; }
    public required string WindowsCommand { get; init; }
}