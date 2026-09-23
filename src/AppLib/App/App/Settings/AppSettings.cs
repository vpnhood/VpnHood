using VpnHood.AppLib.Api.Settings;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.App.Settings;

public class AppSettings
{
    public int Version { get; set; } = 2;
    public string ClientId { get; set; } = Guid.NewGuid().ToString();
    public bool IsNotificationRequested { get; set; }
    public UserReview? UserReview { get; set; }
    public bool IsStartupTrackerSent { get; set; }
    // A UI detects a settings change by comparing ConfigTime between polls, so it needs the full
    // clock precision: FastDateTime's one-second grain would hide two saves in the same second.
    public DateTime ConfigTime { get; set; } = DateTime.UtcNow;
    public UserSettings UserSettings { get; set; } = new();

    internal AppSettingsService? AppSettingsService { get; set; }

    public void Save()
    {
        if (AppSettingsService == null)
            throw new InvalidOperationException("AppSettingsService is not set");

        AppSettingsService.Save();
    }
}