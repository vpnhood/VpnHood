namespace VpnHood.Client.App.Settings;

public class AppLogSettings
{
    public bool LogToConsole { get; set; } = true;
    public bool LogToFile { get; set; }
    public bool LogVerbose { get; set; }
    public bool LogAnonymous { get; set; } = true;
}