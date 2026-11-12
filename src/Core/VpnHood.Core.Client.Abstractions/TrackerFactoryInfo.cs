using System.Text.Json;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Represents a tracker factory as the VPN engine receives it: its kind and its settings.
/// </summary>
/// <remarks>An engine makes no tracker of a kind it does not have, an empty one included.</remarks>
public class TrackerFactoryInfo
{
    /// <summary>
    /// Gets the factory's kind, a name any engine may know (<c>TrackerKinds</c> in
    /// <c>VpnHood.Core.Client.VpnServices.Abstractions.Tracking</c>). The default is empty.
    /// </summary>
    public string Kind { get; init; } = "";

    /// <summary>Gets the factory's settings, as its kind has them.</summary>
    public required JsonElement Settings { get; init; }
}
