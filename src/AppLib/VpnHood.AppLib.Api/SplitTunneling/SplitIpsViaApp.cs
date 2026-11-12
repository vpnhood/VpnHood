namespace VpnHood.AppLib.Api.SplitTunneling;

/// <summary>Represents the IP ranges of the app's own split tunneling, as text.</summary>
public class SplitIpsViaApp
{
    /// <summary>Gets or sets the ranges that go through the tunnel, one per line.</summary>
    public required string Includes { get; set; }

    /// <summary>Gets or sets the ranges that go outside the tunnel, one per line.</summary>
    public required string Excludes { get; set; }

    /// <summary>Gets or sets the ranges that are blocked, one per line.</summary>
    public required string Blocks { get; set; }
}
