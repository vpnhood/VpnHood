using System;
using System.Text;

namespace VpnHood.Server;

internal static class HttpResponses
{
    public static byte[] GetOk()
    {
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n";

        return Encoding.UTF8.GetBytes(response);
    }

    public static byte[] GetUnauthorized()
    {
        var response =
            "HTTP/1.1 401 Unauthorized\r\n" +
            "Server: Kestrel\r\n" +
            "Content-Length: 0\r\n" +
            $"Date: {DateTime.UtcNow:r}\r\n" +
            "WWW-Authenticate: Bearer\r\n" +
            "\r\n";

        return Encoding.UTF8.GetBytes(response);
    }

    public static byte[] GetBadRequest()
    {
        var body =
            "<html>\r\n" +
            "<head>\r\n" +
            "<title>400 Bad Request</title>\r\n" +
            "</head>\r\n" +
            "<body>\r\n" +
            "<center>\r\n" +
            "<h1>400 Bad Request</h1>\r\n" +
            "</html>\r\n";

        var header =
            "HTTP/1.1 400 Bad Request\r\n" +
            "Server: Kestrel\r\n" +
            $"Date: {DateTime.UtcNow:r}\r\n" +
            "Content-Type: text/html\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n";

        var ret = header + "\r\n" + body;
        return Encoding.UTF8.GetBytes(ret);
    }
}