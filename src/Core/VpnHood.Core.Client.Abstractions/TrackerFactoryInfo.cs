using System.Text.Json;

namespace VpnHood.Core.Client.Abstractions;

// A tracker factory as the VPN engine receives it: its kind, a name any engine may know (TrackerKinds in
// VpnHood.Core.Client.VpnServices.Abstractions.Tracking), and its settings as that kind has them. An engine
// makes no tracker of a kind it does not have, an empty one included.
public class TrackerFactoryInfo
{
    public string Kind { get; init; } = "";
    public required JsonElement Settings { get; init; }
}
