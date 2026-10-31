using System.Globalization;
using VpnHood.Net.Http3;

namespace VpnHood.Core.Server.Listeners;

internal sealed class Http3UnauthorizedResponder : IHttp3Responder
{
    public Task Respond(Http3Request request, Http3ResponseWriter response, CancellationToken cancellationToken) =>
        response.WriteHeaders(401, [], cancellationToken);

    public static IReadOnlyList<KeyValuePair<string, string>> CreateHeaders(int statusCode)
    {
        var headers = new List<KeyValuePair<string, string>> {
            new("date", DateTimeOffset.UtcNow.UtcDateTime.ToString("R", CultureInfo.InvariantCulture)),
            new("server", "Kestrel")
        };
        if (statusCode == 401)
            headers.Add(new KeyValuePair<string, string>("www-authenticate", "Bearer"));
        headers.Add(new KeyValuePair<string, string>("content-length", "0"));
        return headers;
    }
}
