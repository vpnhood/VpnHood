using System.Net.Http.Headers;

namespace VpnHood.Client.App.Droid.Connect;

public interface IAuthenticationService: IDisposable
{
    public Task<AuthenticationHeaderValue?> TryGetAuthorization();
    public Task<AuthenticationHeaderValue> SignIn();
    public Task SignOut();

}