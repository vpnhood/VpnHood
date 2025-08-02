using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUiProvider
{
    bool IsQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsAlwaysOnSupported { get; }
    void RequestAlwaysOn(IUiContext uiContext);
    
    SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext);
}