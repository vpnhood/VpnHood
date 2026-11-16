using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Tracking;

/// <summary>
/// Represents what a tracker factory makes a tracker with.
/// </summary>
public class TrackerCreateParams
{
    /// <summary>Gets or sets the client's ID, which the tracker reports as its client.</summary>
    public required string ClientId { get; set; }

    /// <summary>Gets or sets the user agent the tracker sends, or <see langword="null"/> for its own.</summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Gets or sets the person's usage-data switch: a tracker made switched off sends nothing, its start included.
    /// </summary>
    public required bool IsEnabled { get; set; }

    /// <summary>Gets or sets the version the tracker reports: the app's own.</summary>
    [JsonConverter(typeof(VersionConverter))]
    public required Version ClientVersion { get; set; }
}