namespace VpnHood.Server.Access.Managers.HttpAccessManagers;

public class HttpAccessManagerOptions
{
    public HttpAccessManagerOptions(Uri baseUrl, string authorization)
    {
        if (string.IsNullOrEmpty(authorization)) throw new ArgumentNullException(nameof(authorization));

        BaseUrl = baseUrl;
        Authorization = authorization;
    }

    public Uri BaseUrl { get; set; }
    public string Authorization { get; set; }
}