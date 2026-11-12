namespace VpnHood.AppLib.Api.SplitTunneling;

/// <summary>Represents the IP ranges the device's VPN adapter takes or leaves, as text.</summary>
public class SplitIpsViaDevice
{
    /// <summary>Gets or sets the ranges the adapter takes into the tunnel, one per line.</summary>
    public required string Includes { get; set; }

    /// <summary>Gets or sets the ranges the adapter leaves outside the tunnel, one per line.</summary>
    public required string Excludes { get; set; }
}
