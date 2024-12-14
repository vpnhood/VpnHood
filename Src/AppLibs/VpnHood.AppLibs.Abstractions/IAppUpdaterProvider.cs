using VpnHood.Core.Client.Device;

namespace VpnHood.AppLibs.Abstractions;

public interface IAppUpdaterProvider
{
    Task<bool> Update(IUiContext uiContext);
}