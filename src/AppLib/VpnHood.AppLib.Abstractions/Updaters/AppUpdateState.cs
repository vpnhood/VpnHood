namespace VpnHood.AppLib.Abstractions.Updaters;

public class AppUpdateState
{
    public required AppUpdateStage Stage { get; init; }

    // since when the channel has offered the update, where it tells: the store's own update has had
    // that long (Play counts the days it has known it)
    public DateTime? AvailableSince { get; init; }

    public long DownloadedBytes { get; init; }

    // null until the server has told the size
    public long? TotalBytes { get; init; }

    // why the last download or install failed, with AppUpdateStage.Failed
    public string? ErrorMessage { get; init; }

    public static AppUpdateState None { get; } = new() { Stage = AppUpdateStage.None };
}
