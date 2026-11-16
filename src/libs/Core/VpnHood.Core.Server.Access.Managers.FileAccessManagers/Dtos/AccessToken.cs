using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;

/// <summary>Represents an access key as the file access manager keeps it: its ID, secret and limits.</summary>
public class AccessToken
{
    /// <summary>Gets or sets the access key's ID.</summary>
    public required string TokenId { get; set; }

    /// <summary>Gets or sets the access key's secret, which a client proves it holds.</summary>
    public required byte[] Secret { get; set; }

    /// <summary>Gets or sets the access key's name, or <see langword="null"/>.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets when the access key was made, in UTC.</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>Gets or sets when the access key expires, in UTC, or <see langword="null"/> for never.</summary>
    public DateTime? ExpirationTime { get; set; }

    /// <summary>Gets or sets the most devices that may use the access key at once, or 0 for no limit.</summary>
    public int MaxClientCount { get; set; }

    /// <summary>Gets or sets the access key's traffic quota, in bytes, or 0 for none.</summary>
    public long MaxTraffic { get; set; }

    /// <summary>Gets or sets the speed limits each way, in megabits a second, or <see langword="null"/>.</summary>
    public Traffic? MaxSpeedMbps { get; set; }

    /// <summary>
    /// Gets or sets the ad the access key asks for: the answers of <see cref="FileAccessManager.Session_Get"/> and
    /// <see cref="FileAccessManager.Session_GetAll"/> whose error code is <see cref="SessionErrorCode.Ok"/> carry it,
    /// while the answer that makes a session requires none, and the answers to a session's usage and close carry none.
    /// </summary>
    public AdRequirement AdRequirement { get; set; }
}