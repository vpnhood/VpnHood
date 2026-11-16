using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Specifies where a session's DNS servers came from.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know reads as <see cref="UserDns"/>, as it only says which servers
/// answer.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<DnsSelection>))]
[JsonEnumFallback(DnsSelection.UserDns)]
public enum DnsSelection
{
    /// <summary>The user's own.</summary>
    UserDns,

    /// <summary>The server's.</summary>
    ServerDns,

    /// <summary>Google's public DNS.</summary>
    GoogleDns
}
