using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Proxies;

/// <summary>
/// Specifies a proxy endpoint's quality, from its penalty and its successes (<see cref="ProxyEndPointStatus.Quality"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatusQuality>))]
public enum StatusQuality
{
    /// <summary>Never used.</summary>
    Unknown,

    /// <summary>Succeeded, with no penalty.</summary>
    Excellent,

    /// <summary>Succeeded, with a penalty of 1 to 10.</summary>
    Good,

    /// <summary>Succeeded, with a penalty of 11 to 20.</summary>
    Fair,

    /// <summary>Succeeded, with a penalty of 21 to 100.</summary>
    Poor,

    /// <summary>Succeeded, with a penalty of 101 to 10,000.</summary>
    VeryPoor,

    /// <summary>Never succeeded though used, or with a penalty above 10,000.</summary>
    Failed
}
