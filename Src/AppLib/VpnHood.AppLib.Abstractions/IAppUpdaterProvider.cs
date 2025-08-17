using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUpdaterProvider
{
    Task<bool> IsUpdateAvailable(IUiContext uiContext, CancellationToken cancellationToken);

    Task<bool> Update(IUiContext uiContext, CancellationToken cancellationToken);
}