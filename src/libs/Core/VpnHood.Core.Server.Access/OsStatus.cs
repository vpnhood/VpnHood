using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Server.Access;

/// <summary>
/// Represents the OS settings of the server's machine that <see cref="Configurations.OsOptions"/> manages, as the server
/// reads them at each status, and why its last configuration left any of them as they were.
/// </summary>
public class OsStatus
{
    /// <summary>
    /// Gets or sets the TCP congestion control the OS runs, or <see langword="null"/> where the server cannot read it.
    /// </summary>
    public string? TcpCongestionControl { get; set; }

    /// <summary>
    /// Gets or sets the most connections the OS tracks at once, or <see langword="null"/> where the server cannot read it.
    /// </summary>
    public int? ConnectionTrackingMax { get; set; }

    /// <summary>
    /// Gets or sets how long the OS keeps tracking an established TCP connection that sends nothing, or
    /// <see langword="null"/> where the server cannot read it.
    /// </summary>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? ConnectionTrackingTcpIdleTimeout { get; set; }

    /// <summary>
    /// Gets or sets why the server's last configuration left OS settings as they were, one line each: a setting the
    /// machine does not support, a value that is not valid, no admin rights, or a failure. The default is none.
    /// </summary>
    public IReadOnlyList<string> Errors { get; set; } = [];
}
