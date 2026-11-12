namespace VpnHood.AppLib.Abstractions.Device;

/// <summary>Represents the device's proxy settings, as the system tells them.</summary>
public class DeviceProxySettings
{
    /// <summary>Gets the proxy's address.</summary>
    public required Uri ProxyUrl { get; init; }

    /// <summary>Gets the domains that go around the proxy. The default is none.</summary>
    public string[] ExcludeDomains { get; init; } = [];

    /// <summary>Gets the address of the proxy's configuration script (PAC), or <see langword="null"/> for none.</summary>
    public string? PacFileUrl { get; init; }
}