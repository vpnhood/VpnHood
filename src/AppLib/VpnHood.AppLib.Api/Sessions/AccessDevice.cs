namespace VpnHood.AppLib.Api.Sessions;

/// <summary>
/// Represents a device that used an access.
/// </summary>
public class AccessDevice
{
    /// <summary>Gets or sets when the device last used the access, in UTC.</summary>
    public DateTime LastUsedTime { get; set; }

    /// <summary>Gets or sets the device's operating system, or <see langword="null"/>.</summary>
    public string? OperatingSystem { get; set; }

    /// <summary>Gets or sets the device's IP address as last seen, or <see langword="null"/>.</summary>
    public string? IpAddress { get; set; }
}
