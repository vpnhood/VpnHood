using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.Settings;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.App.Settings;

/// <summary>
/// Represents the app's settings as the storage keeps them, in <c>settings.json</c>: the person's settings and what
/// the app notes for itself.
/// </summary>
public class AppSettings
{
    /// <summary>Gets or sets the file's format version. The default is 2.</summary>
    public int Version { get; set; } = 2;

    /// <summary>
    /// Gets or sets the install's ID as an older settings file keeps it, or <see langword="null"/>: the app moves it to
    /// the device-only storage, which a backup does not carry, and clears it here.
    /// </summary>
    [Obsolete("Migration (2026-10): drop a few months after it ships, with VpnHoodApp.MigrateInstallId.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the app has made its one request for notifications, at a connect,
    /// whatever came of it.
    /// </summary>
    public bool IsNotificationRequested { get; set; }

    /// <summary>Gets or sets the person's last review of the app, or <see langword="null"/> for none.</summary>
    public UserReview? UserReview { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the app has made its one report of its first start to the trackers, set
    /// before the report is sent; a report that fails is not sent again.
    /// </summary>
    public bool IsStartupTrackerSent { get; set; }

    /// <summary>
    /// Gets or sets the time of the last save, in UTC, taken before the file is written, at the clock's full precision,
    /// which a UI compares between two reads of the state to see a change
    /// (<see cref="VpnHood.AppLib.Api.App.AppState.ConfigTime"/>).
    /// </summary>
    public DateTime ConfigTime { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the person's settings.</summary>
    public UserSettings UserSettings { get; set; } = new();

    internal AppSettingsService? AppSettingsService { get; set; }

    /// <summary>
    /// Saves the settings, setting off their application first (<see cref="AppSettingsService.Save"/>).
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    /// <exception cref="InvalidOperationException">
    /// The settings are not the app's own, read by its settings service.
    /// </exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save()
    {
        if (AppSettingsService == null)
            throw new InvalidOperationException("AppSettingsService is not set");

        AppSettingsService.Save();
    }
}