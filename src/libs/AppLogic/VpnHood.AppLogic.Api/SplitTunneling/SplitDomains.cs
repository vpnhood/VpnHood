namespace VpnHood.AppLogic.Api.SplitTunneling;

/// <summary>Represents the domains of the split tunneling, as text.</summary>
/// <remarks>
/// An entry matches its domain and every subdomain of it, and takes no <c>*</c>: the lists are refused when saved with
/// one. A list stored before, or edited by hand, is read without an entry's leading <c>*.</c>, which meant the same.
/// Text after <c>#</c> or <c>;</c> is a comment.
/// </remarks>
public class SplitDomains
{
    /// <summary>Gets or sets the domains that go through the tunnel, one per line.</summary>
    public required string Includes { get; set; }

    /// <summary>Gets or sets the domains that go outside the tunnel, one per line.</summary>
    public required string Excludes { get; set; }

    /// <summary>Gets or sets the domains that are blocked, one per line.</summary>
    public required string Blocks { get; set; }
}
