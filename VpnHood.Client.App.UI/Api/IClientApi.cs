using System.Threading.Tasks;
using VpnHood.Client.App.Settings;
// ReSharper disable InconsistentNaming

namespace VpnHood.Client.App.UI.Api;

public interface IClientApi
{
    Task<LoadAppResponse> loadApp(LoadAppParam loadAppParam);
    Task connect(ConnectParam connectParam);
    Task diagnose(ConnectParam connectParam);
    Task disconnect();
    Task<ClientProfile> addAccessKey(AddClientProfileParam addClientProfileParam);
    Task setClientProfile(SetClientProfileParam setClientProfileParam);
    Task removeClientProfile(RemoveClientProfileParam removeClientProfileParam);
    void clearLastError();
    void addTestServer();
    Task setUserSettings(UserSettings userSettings);
    Task log();
}