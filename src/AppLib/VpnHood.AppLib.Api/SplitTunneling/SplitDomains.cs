namespace VpnHood.AppLib.Api.SplitTunneling;

/// <summary>Represents the domains of the split tunneling, as text.</summary>
public class SplitDomains
{
    /// <summary>Gets or sets the domains that go through the tunnel, one per line.</summary>
    public required string Includes { get; set; }

    /// <summary>Gets or sets the domains that go outside the tunnel, one per line.</summary>
    public required string Excludes { get; set; }

    /// <summary>Gets or sets the domains that are blocked, one per line.</summary>
    public required string Blocks { get; set; }
}
