namespace VpnHood.Client.App.Abstractions;

public interface IAppUiService
{
    bool IsQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IAppUiContext uiContext, CancellationToken cancellationToken);
    
    bool IsNotificationSupported { get; }
    Task<bool> RequestNotification(IAppUiContext uiContext, CancellationToken cancellationToken);
    
    bool IsOpenAlwaysOnPageSupported { get; }
    void OpenAlwaysOnPage(IAppUiContext uiContext);
}