using VpnHood.Core.Client.Device;

namespace VpnHood.AppLibs.Abstractions;

public interface IAppUiProvider
{
    bool IsQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsOpenAlwaysOnPageSupported { get; }
    void OpenAlwaysOnPage(IUiContext uiContext);
}