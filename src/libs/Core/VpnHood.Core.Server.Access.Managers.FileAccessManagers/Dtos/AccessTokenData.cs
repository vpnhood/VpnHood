namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;

/// <summary>Represents an access key with its usage, as the file access manager keeps them.</summary>
/// <remarks>
/// It is the cached copy: the store replaces a part once its file is written, so a holder sees the change then.
/// </remarks>
public class AccessTokenData
{
    /// <summary>Gets or sets the access key.</summary>
    public required AccessToken AccessToken { get; set; }

    /// <summary>Gets or sets the access key's usage.</summary>
    public required AccessTokenUsage Usage { get; set; }
}