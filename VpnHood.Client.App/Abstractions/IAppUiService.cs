namespace VpnHood.Client.App.Abstractions;

public interface IAppUiService
{
    bool IsNotificationSupported { get; }
    bool IsQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IAppUiContext uiContext, CancellationToken cancellationToken);
    Task<bool> RequestNotification(IAppUiContext uiContext, CancellationToken cancellationToken);
}