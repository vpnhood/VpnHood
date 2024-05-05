namespace VpnHood.Client.App.Abstractions;

public interface IAppAuthenticationExternalService: IDisposable
{
    public Task<string> SilentSignIn(IAppUiContext uiContext);
    public Task<string> SignIn(IAppUiContext uiContext);
    public Task SignOut(IAppUiContext uiContext);
}