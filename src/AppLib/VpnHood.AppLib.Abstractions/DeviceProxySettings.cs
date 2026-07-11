namespace VpnHood.AppLib.Abstractions;

public class DeviceProxySettings
{
    public required Uri ProxyUrl { get; init; }
    public string[] ExcludeDomains { get; init; } = [];
    public string? PacFileUrl { get; init; }
}