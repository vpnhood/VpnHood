using System.Net;
using System.Net.Mime;
using System.Text;
using VpnHood.Core.Tunneling.WebSockets;

namespace VpnHood.Core.Server;

internal static class HttpResponseBuilder
{
    public static ReadOnlyMemory<byte> Build(HttpResponseMessage httpResponse)
    {
        httpResponse.Headers.Date = DateTimeOffset.UtcNow;

        // add headers
        var responseBuilder = new StringBuilder();
        responseBuilder.Append(
            $"HTTP/{httpResponse.Version.Major}.{httpResponse.Version.Minor} {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}\r\n");
        foreach (var header in httpResponse.Headers)
            responseBuilder.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");

        foreach (var header in httpResponse.Content.Headers)
            responseBuilder.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");

        responseBuilder.Append("\r\n");

        // add content if available
        var context = httpResponse.Content.ReadAsStringAsync().Result;
        responseBuilder.Append(context);

        return Encoding.UTF8.GetBytes(responseBuilder.ToString());
    }

    public static ReadOnlyMemory<byte> Ok(int contentLength)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content.Headers.ContentLength = contentLength;
        return Build(response);
    }

    public static ReadOnlyMemory<byte> Http01(string keyAuthorization)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.ConnectionClose = true;
        response.Content = new StringContent(keyAuthorization, Encoding.UTF8, MediaTypeNames.Text.Plain);
        return Build(response);
    }

    public static ReadOnlyMemory<byte> WebSocketUpgrade(string webSocketKey)
    {
        var response = new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
        response.Headers.Add("Upgrade", "websocket");
        response.Headers.Connection.Add("Upgrade");
        response.Headers.Add("Sec-WebSocket-Accept", WebSocketUtils.ComputeWebSocketAccept(webSocketKey));
        return Build(response);
    }

    public static ReadOnlyMemory<byte> ServiceUnavailable()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.ConnectionClose = true;
        return Build(response);
    }
}