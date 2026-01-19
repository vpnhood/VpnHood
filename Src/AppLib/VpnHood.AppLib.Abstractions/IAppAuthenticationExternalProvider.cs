using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppAuthenticationExternalProvider : IDisposable
{
    public Task<string> SignIn(IUiContext uiContext, bool isSilentLogin, CancellationToken cancellationToken);
    public Task SignOut(IUiContext uiContext, CancellationToken cancellationToken);
}