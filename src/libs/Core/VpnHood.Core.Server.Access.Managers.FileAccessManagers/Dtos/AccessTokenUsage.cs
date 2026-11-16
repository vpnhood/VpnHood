using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;

/// <summary>Represents an access key's usage, as the file access manager keeps it.</summary>
public class AccessTokenUsage
{
    /// <summary>Gets or sets the record's format version: 2 now, as a record below it took its times from its file.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets when the usage began, in UTC. The default is the time it is made.</summary>
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the access key's usage was last recorded, in UTC, one of no bytes too. The default is the time it
    /// is made.
    /// </summary>
    public DateTime LastUsedTime { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the bytes the access key's sessions sent.</summary>
    public long Sent { get; set; }

    /// <summary>Gets or sets the bytes the access key's sessions received.</summary>
    public long Received { get; set; }

    internal Traffic ToTraffic() => new() { Sent = Sent, Received = Received };
}