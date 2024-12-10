using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppUiProvider
{
    bool IsQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsOpenAlwaysOnPageSupported { get; }
    void OpenAlwaysOnPage(IUiContext uiContext);
}