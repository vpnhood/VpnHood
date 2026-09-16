using System.Text.Json;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.Clients;

// The six HTTP clients call the toolkit's ApiClientBase directly - it owns the request, the url and
// its query, the logging and the failure. All this adds is the two ends that are the app's own: the
// JSON, camelCase as the server writes it and source-generated because the browser is published
// trimmed and a type the trimmer cannot see is a type it removes; and the exception, rebuilt from
// the ApiError the server sent so a UI catches the same type whether the call crossed HTTP or not.
// Paths are relative to the client's base address, which is where the page was served from, so the
// cookie that carries the pairing goes with every call by itself.
internal abstract class AppApiClientBase(HttpClient httpClient) : ApiClientBase(httpClient)
{
    protected override JsonSerializerOptions CreateSerializerSettings()
    {
        return new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = AppApiJsonContext.Default
        };
    }

    // The server's ApiError as the exception it names - a NotExistsException for a missing profile,
    // an InvalidOperationException for a bad key - with its Data, as the in-process call would have
    // thrown it. A name the toolkit cannot build stays an ApiException carrying the name and the
    // status: no UI branches on an app exception type, it reads the message and the name.
    protected override Exception? ToException(ApiError apiError)
    {
        var exception = apiError.ToException();
        return exception is ApiException ? null : exception;
    }
}
