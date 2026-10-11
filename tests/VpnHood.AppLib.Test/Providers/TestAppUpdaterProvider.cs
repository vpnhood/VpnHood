using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>A store's updater double: it shows its own screen, so every call needs the window.</summary>
internal class TestAppUpdaterProvider : IAppUpdaterProvider
{
    public int IsUpdateAvailableCalls { get; private set; }

    public Task<bool> IsUpdateAvailable(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        IsUpdateAvailableCalls++;
        return Task.FromResult(false);
    }

    public Task<bool> Update(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        return Task.FromResult(false);
    }
}
