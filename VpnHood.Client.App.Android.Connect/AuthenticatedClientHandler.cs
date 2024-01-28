namespace VpnHood.Client.App.Droid.Connect;

public class AuthenticatedClientHandler(IAuthenticationService authenticationService) : HttpClientHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = await authenticationService.TryGetAuthorization();
        
        return await base.SendAsync(request, cancellationToken);
    }
}