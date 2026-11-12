namespace VpnHood.AppLib.Abstractions.Updaters;

/// <summary>Represents an update of the app as it stands.</summary>
public class AppUpdateState
{
    /// <summary>Gets where the update stands.</summary>
    public required AppUpdateStage Stage { get; init; }

    /// <summary>
    /// Gets since when the channel has offered the update, in UTC, where it tells, or <see langword="null"/>: a store's
    /// own update has had that long, as Play counts the days it has known it.
    /// </summary>
    public DateTime? AvailableSince { get; init; }

    /// <summary>
    /// Gets the bytes of the update downloaded so far, as the channel tells them while it downloads; 0 where it tells
    /// none, as in the stages after.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Gets the update's size, in bytes, as the channel tells it while it downloads, or <see langword="null"/> where it
    /// tells none, as before the server has told it and in the stages after.
    /// </summary>
    public long? TotalBytes { get; init; }

    /// <summary>
    /// Gets why the last download or install failed, with <see cref="AppUpdateStage.Failed"/>, or <see langword="null"/>.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets a value indicating whether, with <see cref="AppUpdateStage.Downloaded"/>, the system still has to let the
    /// app install apps, which Install asks first, as Android's "Install unknown apps", so the person's next Install
    /// installs.
    /// </summary>
    public bool IsInstallPermissionNeeded { get; init; }

    /// <summary>Gets the state of no update.</summary>
    public static AppUpdateState None { get; } = new() { Stage = AppUpdateStage.None };
}
