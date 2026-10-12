using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// Each call takes the window, or null where none is open, as in the desktop service: a provider
/// that needs one throws <c>UiContextNotAvailableException</c>, and the check waits for one that has it.
/// </summary>
public interface IAppUpdaterProvider
{
    Task<bool> IsUpdateAvailable(IUiContext? uiContext, CancellationToken cancellationToken);

    Task<bool> Update(IUiContext? uiContext, CancellationToken cancellationToken);

    // Brings back the screen of an update the person started and left before it finished; false when
    // there is none. Asked with a window in front, apart from the checks' interval.
    Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken);
}