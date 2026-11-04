using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

// A name this side does not know reads as None: no ad; a server that needs one ends the session.
[JsonConverter(typeof(JsonStringEnumFallbackConverter<AdRequirement>))]
[JsonEnumFallback(AdRequirement.None)]
public enum AdRequirement
{
    None,
    Flexible,
    Rewarded
}