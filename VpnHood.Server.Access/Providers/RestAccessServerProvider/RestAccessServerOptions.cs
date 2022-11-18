using System;

namespace VpnHood.Server.Providers.RestAccessServerProvider;

public class RestAccessServerOptions
{
    public RestAccessServerOptions(Uri baseUrl, string authorization)
    {
        if (string.IsNullOrEmpty(authorization)) throw new ArgumentNullException(nameof(authorization));

        BaseUrl = baseUrl;
        Authorization = authorization;
    }

    public Uri BaseUrl { get; set; }
    public string Authorization { get; set; }
}