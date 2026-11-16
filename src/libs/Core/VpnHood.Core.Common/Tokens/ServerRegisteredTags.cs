namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// Provides the registered tags of servers, which their locations carry and MANAGER's server selection reads.
/// </summary>
public static class ServerRegisteredTags
{
    /// <summary>A location of premium servers.</summary>
    public const string Premium = "#premium";

    /// <summary>A location of servers meant to work where VPNs are blocked.</summary>
    public const string Unblockable = "#unblockable";
}