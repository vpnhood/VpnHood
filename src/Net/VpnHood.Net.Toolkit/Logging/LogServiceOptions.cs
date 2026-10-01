using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

public class LogServiceOptions
{
    // When false, Start is a no-op: no file joins the process's log and its filters are left as
    // they are. Used when many apps share one process (tests).
    public bool Enabled { get; set; } = true;
    public bool LogToFile { get; set; } = true;
    public bool? LogAnonymous { get; set; }
    public bool AutoFlush { get; set; } = true;
    public string[] LogEventNames { get; set; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
    public LogLevel MinLogLevel { get; set; } = LogLevel.Information;
}
