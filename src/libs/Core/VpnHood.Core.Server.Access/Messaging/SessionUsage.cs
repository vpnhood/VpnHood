using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access.Messaging;

/// <summary>
/// Represents a session's traffic since its last report, or its close, as the server sends them to the access manager.
/// </summary>
public class SessionUsage
{
    /// <summary>Gets or sets the session's ID.</summary>
    public required ulong SessionId { get; set; }

    /// <summary>Gets or sets a value indicating whether the session closed, for older readers.</summary>
    [Obsolete("Use ErrorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Closed { get; set; }

    /// <summary>
    /// Gets or sets the error the session closed with, or <see cref="SessionErrorCode.Ok"/> where it goes on.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SessionErrorCode ErrorCode { get; set; }

    /// <summary>Gets or sets the session's sent bytes since its last report.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Sent { get; set; }

    /// <summary>Gets or sets the session's received bytes since its last report.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Received { get; set; }

    /// <summary>Gets or sets a rewarded ad's data, or <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AdData { get; set; }

    /// <summary>Gets the usage's bytes as traffic.</summary>
    /// <returns>The sent and received bytes.</returns>
    public Traffic ToTraffic() => new(Sent, Received);
}