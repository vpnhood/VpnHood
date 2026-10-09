using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

public class TrackerCreateParams
{
    public required string ClientId { get; set; }
    public string? UserAgent { get; set; }

    // The person's usage-data switch: a tracker made switched off sends nothing, its start included.
    public required bool IsEnabled { get; set; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version ClientVersion { get; set; }
}