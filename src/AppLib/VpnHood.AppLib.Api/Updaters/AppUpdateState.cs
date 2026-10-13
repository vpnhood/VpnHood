namespace VpnHood.AppLib.Api.Updaters;

// The update the notice offers where the app updates itself: the feed's newer version, and how far
// its update has come
public class AppUpdateState
{
    public required Version Version { get; init; }
    public required AppUpdateStage Stage { get; init; }
    public required long DownloadedBytes { get; init; }

    // null until the server has told the size
    public required long? TotalBytes { get; init; }

    // why the last download or install failed, with AppUpdateStage.Failed
    public required string? ErrorMessage { get; init; }
}
