namespace VpnHood.AppLib.Abstractions;

public class DeviceProxySettings
{
    public required string Host { get; set; }
    public required int Port { get; set; }
    public string[] ExcludeDomains { get; set; } = [];
    public string? PacFileUrl { get; set; }
}