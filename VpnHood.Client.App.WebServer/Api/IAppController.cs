using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.WebServer.Api;

public interface IAppController
{
    Task<AppConfig> Configure(ConfigParams configParams);
    Task<AppConfig> GetConfig();
    Task<AppState> GetState();
    Task Connect(Guid? clientProfileId = null, string? serverLocation = null, string? planId = null);
    Task Diagnose(Guid? clientProfileId = null, string? serverLocation = null, string? planId = null);
    Task Disconnect();
    void ClearLastError();
    Task SetUserSettings(UserSettings userSettings);
    Task<string> Log();
    Task<DeviceAppInfo[]> GetInstalledApps();
    Task VersionCheck();
    void VersionCheckPostpone();
    void OpenAlwaysOnPage();
    Task RequestQuickLaunch();
    Task RequestNotification();
}