namespace VpnHood.AppLib.Services.Updaters;

internal class AppUpdaterData
{
    public Version? PostponeVersion { get; set; }
    public DateTime? PostponeTime { get; set; }
    public DateTime? CheckedTime { get; set; }
    public PublishInfo? PublishInfo { get; set; }
    public DateTime? UpdaterAvailableSince { get; set; }
    public bool Prompt { get; init; }

}