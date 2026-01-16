using VpnHood.Core.IpLocations;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.Settings;

public class AppSettings
{
    public int Version { get; set; } = 2;
    public string ClientId { get; set; } = Guid.NewGuid().ToString();
    public bool IsNotificationRequested { get; set; }
    public UserReview? UserReview { get; set; }
    public bool IsStartupTrackerSent { get; set; }
    public IpLocation? ClientIpLocation { get; set; }
    public IpLocation? ClientIpLocationByServer { get; set; }
    public DateTime ConfigTime { get; set; } = DateTime.Now;
    public UserSettings UserSettings { get; set; } = new();

    internal AppSettingsService? AppSettingsService { get; set; }

    public void Save()
    {
        if (AppSettingsService == null)
            throw new InvalidOperationException("AppSettingsService is not set");

        AppSettingsService.Save();
    }
}