using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppAuthenticationExternalProvider : IDisposable
{
    public Task<string> SignIn(IUiContext uiContext, bool isSilentLogin);
    public Task SignOut(IUiContext uiContext);
}