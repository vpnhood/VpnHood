using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;

namespace VpnHood.Server;

internal static class HttpResponseBuilder
{
    public static byte[] Build(HttpResponseMessage httpResponse)
    {
        var context = httpResponse.Content.ReadAsStringAsync().Result;
        httpResponse.Headers.Date = DateTimeOffset.Now;
        httpResponse.Content.Headers.ContentLength = context.Length;

        var response =
            $"HTTP/{httpResponse.Version.Major}.{httpResponse.Version.Minor} {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}\r\n";
        response = httpResponse.Headers.Aggregate(response,
            (current, header) => current + $"{header.Key}: {string.Join(", ", header.Value)}\r\n");
        response = httpResponse.Content.Headers.Aggregate(response,
            (current, header) => current + $"{header.Key}: {string.Join(", ", header.Value)}\r\n");
        response += "\r\n" + context;
        return Encoding.UTF8.GetBytes(response);
    }

    public static byte[] Ok()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        return Build(response);
    }

    public static byte[] Unauthorized()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer"));
        return Build(response);
    }

    public static byte[] Http01(string keyAuthorization)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.ConnectionClose = true;
        response.Content = new StringContent(keyAuthorization, Encoding.Default, MediaTypeNames.Text.Plain);
        return Build(response);
    }

    public static byte[] BadRequest()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Headers.ConnectionClose = true;
        return Build(response);
    }

    public static byte[] NotFound()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        response.Headers.ConnectionClose = true;
        return Build(response);
    }
}