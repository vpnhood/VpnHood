using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides the user's log settings, which <see cref="LogService"/> applies.
/// </summary>
public class LogServiceOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether <see cref="LogService.Start"/> applies the options; off, it does nothing,
    /// as where many apps share one process, such as tests. The default is <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the log goes to the file. The default is <see langword="true"/>.</summary>
    public bool LogToFile { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether identifying values are redacted, or <see langword="null"/> for yes.
    /// </summary>
    public bool? LogAnonymous { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is flushed at every line. The default is <see langword="true"/>.
    /// </summary>
    public bool AutoFlush { get; set; } = true;

    /// <summary>
    /// Gets or sets the names of the events to log, for the app to apply; <see cref="LogService"/> does not filter by them.
    /// The default is none.
    /// </summary>
    public string[] LogEventNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the lowest level logged; in JSON, an unknown level reads as Information. The default is Information.
    /// </summary>
    [JsonConverter(typeof(LogLevelJsonConverter))]
    public LogLevel MinLogLevel { get; set; } = LogLevel.Information;
}
