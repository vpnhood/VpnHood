using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Specifies the ad a session requires before it carries traffic.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know reads as <see cref="None"/>, so the client shows no ad, and
/// a server that needs one ends the session.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<AdRequirement>))]
[JsonEnumFallback(AdRequirement.None)]
public enum AdRequirement
{
    /// <summary>No ad.</summary>
    None,

    /// <summary>An ad if the app can show one; a failure to show it does not end the session.</summary>
    Flexible,

    /// <summary>A rewarded ad, which the user must watch to the end.</summary>
    Rewarded
}