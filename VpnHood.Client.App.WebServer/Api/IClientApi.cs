using System;
using System.Threading.Tasks;
using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
// ReSharper disable InconsistentNaming

namespace VpnHood.Client.App.WebServer.Api;

public interface IClientApi
{
    Task<AppConfig> GetConfig();
    Task<AppState> GetState();
    Task Connect(Guid? clientProfileId);
    Task Diagnose(Guid? clientProfileId);
    Task Disconnect();
    Task<ClientProfile> AddAccessKey(string accessKey);
    Task UpdateClientProfile(Guid clientProfileId, ClientProfileUpdateParams updateParams);
    Task DeleteClientProfile(Guid clientProfileId);
    void ClearLastError();
    void AddTestServer();
    Task SetUserSettings(UserSettings userSettings);
    Task Log();
    Task<DeviceAppInfo[]> GetInstalledApps();
    Task<IpGroup[]> GetIpGroups();
}