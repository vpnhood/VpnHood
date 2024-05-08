using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppUpdaterService
{
    Task<bool> Update(IUiContext uiContext);
}