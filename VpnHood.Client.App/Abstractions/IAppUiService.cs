namespace VpnHood.Client.App.Abstractions;

public interface IAppUiService
{
    bool IsNotificationSupported { get; }
    bool IsQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(CancellationToken cancellationToken);
    Task<bool> RequestNotification(CancellationToken cancellationToken);
}