using VpnHood.Client.Device;

namespace VpnHood.AppFramework.Abstractions;

public interface IAppAuthenticationExternalProvider : IDisposable
{
    public Task<string> SignIn(IUiContext uiContext, bool isSilentLogin);
    public Task SignOut(IUiContext uiContext);
}