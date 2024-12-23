using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace VpnHood.AppLib.Settings;

public class AppLogOptions
{
    public bool LogToConsole { get; set; } = true;
    public bool LogToFile { get; set; }
    public bool LogAnonymous { get; set; } = true;
    public bool AutoFlush { get; set; }
    public string[] LogEventNames { get; set; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}