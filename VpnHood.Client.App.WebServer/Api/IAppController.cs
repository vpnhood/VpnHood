using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
using VpnHood.Common.Tokens;

namespace VpnHood.Client.App.WebServer.Api;

public interface IAppController
{
    Task<AppConfig> Configure(ConfigParams configParams);
    Task<AppConfig> GetConfig();
    Task<AppState> GetState();
    Task Connect(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId);
    Task Diagnose(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId);
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
    Task ShowRewardedAd();
}