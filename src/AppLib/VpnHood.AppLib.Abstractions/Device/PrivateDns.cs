namespace VpnHood.AppLib.Abstractions.Device;

/// <summary>Represents the device's private DNS: DNS over TLS that the system sends itself.</summary>
public class PrivateDns
{
    /// <summary>Gets or sets a value indicating whether the private DNS is in use.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the private DNS server's host name, or <see langword="null"/> where the system chooses it.</summary>
    public string? Provider { get; set; }
}