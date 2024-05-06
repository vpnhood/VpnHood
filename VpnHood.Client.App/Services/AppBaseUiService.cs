using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Services;

internal class AppBaseUiService 
    : IAppUiService
{
    public bool IsQuickLaunchSupported => false;
    public Task<bool> RequestQuickLaunch(IAppUiContext uiContext, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool IsNotificationSupported => false;
    public Task<bool> RequestNotification(IAppUiContext uiContext, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool IsOpenAlwaysOnPageSupported => false;
    public void OpenAlwaysOnPage(IAppUiContext uiContext) => throw new NotSupportedException();
}