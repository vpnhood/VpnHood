using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace VpnHood.Client.App.Settings;

public class AppLogSettings
{
    public bool LogToConsole { get; set; } = true;
    public bool LogToFile { get; set; }
    public bool LogAnonymous { get; set; } = true;
    public string[] LogEventNames { get; set; }  = [];

    [JsonConverter(typeof(JsonStringEnumConverter))] 
    public LogLevel LogLevel { get; set; }  = LogLevel.Information;
}