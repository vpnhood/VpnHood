using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class LogServiceOptions
{
    public bool LogToConsole { get; set; } = true;
    public bool LogToFile { get; set; } = true;
    public bool? LogAnonymous { get; set; } 
    public bool AutoFlush { get; set; } = true;
    public string[] LogEventNames { get; set; } = [];
    public bool SingleLineConsole { get; set; } = true;
    public string? CategoryName { get; set; } = "VpnHood";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}