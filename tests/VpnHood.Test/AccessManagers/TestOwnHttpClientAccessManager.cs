using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;

namespace VpnHood.Test.AccessManagers;

// an access manager made from its options, with a look at the HTTP client it made itself
public class TestOwnHttpClientAccessManager(HttpAccessManagerOptions options) : HttpAccessManager(options)
{
    public HttpClient Client => HttpClient ?? throw new InvalidOperationException("The access manager has no client.");
}
