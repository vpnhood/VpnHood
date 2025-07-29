using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUpdaterProvider
{
    Task<bool> Update(IUiContext uiContext, CancellationToken cancellationToken);
}