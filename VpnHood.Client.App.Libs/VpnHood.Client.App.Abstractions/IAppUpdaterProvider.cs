using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppUpdaterProvider
{
    Task<bool> Update(IUiContext uiContext);
}