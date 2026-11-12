namespace VpnHood.Core.Server.Access.Managers.HttpAccessManagers;

/// <summary>
/// Represents where an HTTP access manager is, and how the server proves itself to it.
/// </summary>
public class HttpAccessManagerOptions
{
    /// <summary>Initializes a new instance of the <see cref="HttpAccessManagerOptions"/> class.</summary>
    /// <param name="baseUrl">The access manager's address, of which its scheme, host and port are used.</param>
    /// <param name="authorization">The value of the Authorization header, as a scheme and its credentials.</param>
    /// <exception cref="ArgumentNullException"><paramref name="authorization"/> is null or empty.</exception>
    public HttpAccessManagerOptions(Uri baseUrl, string authorization)
    {
        if (string.IsNullOrEmpty(authorization)) throw new ArgumentNullException(nameof(authorization));

        BaseUrl = baseUrl;
        Authorization = authorization;
    }

    /// <summary>Gets or sets the access manager's address, of which its scheme, host and port are used.</summary>
    public Uri BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the value of the Authorization header, as a scheme and its credentials; one that does not parse is
    /// not sent.
    /// </summary>
    public string Authorization { get; set; }
}