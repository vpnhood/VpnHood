using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access.Messaging;

/// <summary>
/// Represents a session as the access manager gives it to the server: what the client gets, with what only the server
/// needs.
/// </summary>
public class SessionResponseEx : SessionResponse
{
    /// <summary>Gets or sets the protocol version the session runs on, as its request gave it.</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>Gets or sets the server's own data for the session, as its request gave it, or <see langword="null"/>.</summary>
    public string? ExtraData { get; set; }

    /// <summary>
    /// Gets or sets the GA4 measurement ID the client reports the session to, where its person allows it, or
    /// <see langword="null"/>.
    /// </summary>
    public string? GaMeasurementId { get; set; }

    /// <summary>Gets or sets when the session was made, in UTC, or <see langword="null"/>.</summary>
    public DateTime? CreatedTime { get; set; }

    /// <summary>Gets or sets the session's ID.</summary>
    public ulong SessionId { get; set; }

    /// <summary>
    /// Gets or sets the session's key, which the client's requests in the session carry. The default is empty.
    /// </summary>
    public byte[] SessionKey { get; set; } = [];

    /// <summary>Gets or sets the server's location, which the client gets, or <see langword="null"/>.</summary>
    public string? ServerLocation { get; set; }

    /// <summary>Gets or sets the server's tags, which the client gets. The default is none.</summary>
    public string[] ServerTags { get; set; } = [];

    /// <summary>Gets or sets the access's terms and state, which the client gets, or <see langword="null"/>.</summary>
    public AccessInfo? AccessInfo { get; set; }

    /// <summary>Gets or sets the ad the session requires. The default is none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AdRequirement AdRequirement { get; set; } = AdRequirement.None;

    /// <summary>Gets or sets whose session this one took the place of, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SessionSuppressType SuppressedTo { get; set; }
}