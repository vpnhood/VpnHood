using System;

namespace VpnHood.Server.Providers.HttpAccessServerProvider;

public class HttpAccessServerOptions
{
    public HttpAccessServerOptions(Uri baseUrl, string authorization)
    {
        if (string.IsNullOrEmpty(authorization)) throw new ArgumentNullException(nameof(authorization));

        BaseUrl = baseUrl;
        Authorization = authorization;
    }

    public Uri BaseUrl { get; set; }
    public string Authorization { get; set; }
}