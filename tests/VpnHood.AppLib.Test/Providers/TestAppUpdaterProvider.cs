using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>A store's updater double: it shows its own screen, so every call needs the window.</summary>
internal class TestAppUpdaterProvider : IAppUpdaterProvider
{
    public int IsUpdateAvailableCalls { get; private set; }
    public int UpdateCalls { get; private set; }

    // What the store answers. False also stands for a store out of reach: Play answers "none" then.
    public bool IsAvailable { get; set; }

    // What its screen answers: false for one that failed.
    public bool IsUpdateShown { get; set; } = true;

    // An update the person started and left before it finished.
    public bool IsUpdateInProgress { get; set; }
    public int ResumeUpdateCalls { get; private set; }

    public Task<bool> IsUpdateAvailable(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        IsUpdateAvailableCalls++;
        return Task.FromResult(IsAvailable);
    }

    // the store's screen
    public Task<bool> Update(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        UpdateCalls++;
        return Task.FromResult(IsUpdateShown);
    }

    // the screen of an update in progress, brought back
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        if (!IsUpdateInProgress)
            return Task.FromResult(false);

        ResumeUpdateCalls++;
        return Task.FromResult(true);
    }
}
