using VpnHood.Client.Device;

namespace VpnHood.AppFramework.Abstractions;

public interface IAppUpdaterProvider
{
    Task<bool> Update(IUiContext uiContext);
}