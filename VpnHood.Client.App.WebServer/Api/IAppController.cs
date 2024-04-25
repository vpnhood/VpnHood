using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.WebServer.Api;

public interface IAppController
{
    Task<AppConfig> Configure(ConfigParams configParams);
    Task<AppConfig> GetConfig();
    Task<AppState> GetState();
    Task Connect(Guid? clientProfileId = null, string? regionId = null);
    Task Diagnose(Guid? clientProfileId = null, string? regionId = null);
    Task Disconnect();
    Task<ClientProfileInfo> AddAccessKey(string accessKey);
    Task UpdateClientProfile(Guid clientProfileId, ClientProfileUpdateParams updateParams);
    Task DeleteClientProfile(Guid clientProfileId);
    void ClearLastError();
    Task SetUserSettings(UserSettings userSettings);
    Task<string> Log();
    Task<DeviceAppInfo[]> GetInstalledApps();
    Task<IpGroup[]> GetIpGroups();
    Task VersionCheck();
    void VersionCheckPostpone();
}