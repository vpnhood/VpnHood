using VpnHood.Core.Client.Device;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUiProvider
{
    bool IsQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsOpenAlwaysOnPageSupported { get; }
    void OpenAlwaysOnPage(IUiContext uiContext);
    
    SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext);
}