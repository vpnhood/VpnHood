using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppAuthenticationExternalService : IDisposable
{
    public Task<string> SilentSignIn(IUiContext uiContext);
    public Task<string> SignIn(IUiContext uiContext);
    public Task SignOut(IUiContext uiContext);
}