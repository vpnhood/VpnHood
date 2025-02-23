using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

public class TrackerCreateParams
{
    public required string ClientId { get; set; }
    public string? UserAgent { get; set; }
    public string? Ga4MeasurementId { get; set; }

    [JsonConverter(typeof(VersionConverter))]
    public required Version ClientVersion { get; set; }

}