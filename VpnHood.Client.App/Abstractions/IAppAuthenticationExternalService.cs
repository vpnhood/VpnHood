namespace VpnHood.Client.App.Abstractions;

public interface IAppAuthenticationExternalService: IDisposable
{
    public Task<string?> TryGetIdToken();
    public Task<string> SignIn();
    public Task SignOut();

}