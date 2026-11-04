using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

// A name this side does not know reads as None: nothing suppressed.
[JsonConverter(typeof(JsonStringEnumFallbackConverter<SessionSuppressType>))]
[JsonEnumFallback(SessionSuppressType.None)]
public enum SessionSuppressType
{
    None,
    YourSelf,
    Other
}