using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Sessions;

/// <summary>
/// Specifies where a session's DNS servers came from.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know fails to read.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DnsSelection>))]
public enum DnsSelection
{
    /// <summary>The user's own.</summary>
    UserDns,

    /// <summary>The server's.</summary>
    ServerDns,

    /// <summary>Google's public DNS.</summary>
    GoogleDns
}
