using VpnHood.Core.Client.Device;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUpdaterProvider
{
    Task<bool> Update(IUiContext uiContext);
}