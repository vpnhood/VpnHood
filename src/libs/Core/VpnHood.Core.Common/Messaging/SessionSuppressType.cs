using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Specifies whose newer session took a session's place.
/// </summary>
/// <remarks>In JSON it is a name; a name this version does not know reads as <see cref="None"/>.</remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<SessionSuppressType>))]
[JsonEnumFallback(SessionSuppressType.None)]
public enum SessionSuppressType
{
    /// <summary>None.</summary>
    None,

    /// <summary>
    /// The same client's: a client has one session of an access at a time, where the access manager holds it to that,
    /// as MANAGER does but for a public access.
    /// </summary>
    YourSelf,

    /// <summary>Another client's, which went over the access's limit of clients.</summary>
    Other
}