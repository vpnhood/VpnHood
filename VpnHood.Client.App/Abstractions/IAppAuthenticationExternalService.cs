namespace VpnHood.Client.App.Abstractions;

public interface IAppAuthenticationExternalService: IDisposable
{
    public Task<string?> TrySilentSignIn();
    public Task<string> SignIn();
    public Task SignOut();

}