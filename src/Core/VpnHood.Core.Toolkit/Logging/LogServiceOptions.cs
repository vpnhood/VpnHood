using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class LogServiceOptions
{
    // When false, Start is a no-op: no sinks are created and the process-wide VhLogger is left
    // untouched. Used when many components share one process (e.g. tests running many apps).
    public bool Enabled { get; set; } = true;
    public bool LogToConsole { get; set; } = true;
    public bool LogToDevice { get; set; } = true;
    public bool LogToFile { get; set; } = true;
    public bool? LogAnonymous { get; set; }
    public bool AutoFlush { get; set; } = true;
    public string[] LogEventNames { get; set; } = [];
    public bool SingleLineConsole { get; set; } = true;
    public string? CategoryName { get; set; } = "VpnHood";

    [JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
    public LogLevel MinLogLevel { get; set; } = LogLevel.Information;
}