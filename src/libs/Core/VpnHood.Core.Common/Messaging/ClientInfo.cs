namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents a client as it introduces itself to a server.
/// </summary>
public class ClientInfo
{
    /// <summary>Gets the client's ID, the same for its every session.</summary>
    public required string ClientId { get; init; }

    /// <summary>Gets the client's version.</summary>
    public required string ClientVersion { get; init; }

    /// <summary>Gets the oldest protocol version the client speaks.</summary>
    public int MinProtocolVersion { get; init; }

    /// <summary>Gets the newest protocol version the client speaks.</summary>
    public int MaxProtocolVersion { get; init; }

    /// <summary>Gets the client's user agent, which names its app and platform.</summary>
    public required string UserAgent { get; init; }

    /// <summary>Writes the client's ID, as <c>ClientId=...</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return $"{nameof(ClientId)}={ClientId}";
    }
}