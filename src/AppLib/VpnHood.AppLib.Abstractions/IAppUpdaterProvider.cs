using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUpdaterProvider
{
    Task<bool> IsUpdateAvailable(IUiContext uiContext, CancellationToken cancellationToken);

    Task<bool> Update(IUiContext uiContext, CancellationToken cancellationToken);
}