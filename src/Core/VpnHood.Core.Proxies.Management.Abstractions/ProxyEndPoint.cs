using System.Security.Cryptography;
using System.Text;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Represents a proxy server: its protocol, address, credentials and whether it is used.
/// </summary>
public class ProxyEndPoint
{
    /// <summary>
    /// Gets the endpoint's ID: the hex MD5 hash of its protocol's name, host and port, as in
    /// <c>Socks5://host:1080</c>. The host is hashed as written, so a change of its case gives another ID.
    /// </summary>
    public string Id {
        get {
            // it is just hash. ipv6 don't need special handling
            var id = $"{Protocol}://{Host}:{Port}";
            var md5 = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(id);
            var hashBytes = md5.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes);
        }
    }

    /// <summary>Gets or sets a value indicating whether the endpoint is used. The default is <see langword="true"/>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets the proxy's protocol.</summary>
    public required ProxyProtocol Protocol { get; init; }

    /// <summary>Gets the proxy's host name or IP address.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the proxy's port.</summary>
    public required int Port { get; init; }

    /// <summary>Gets or sets the user name, or <see langword="null"/>.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password, or <see langword="null"/>.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets the endpoint's URL, credentials included, as <c>socks5://user:password@host:1080/?enabled=1</c>. The
    /// query is there only for an enabled endpoint.
    /// </summary>
    public Uri Url =>
        new UriBuilder {
            Scheme = Protocol.ToString().ToLower(),
            Host = Host,
            Port = Port,
            UserName = Username,
            Password = Password,
            Query = IsEnabled ? "enabled=1" : null
        }.Uri;

    /// <summary>Builds the endpoint's URL with its scheme, host and port alone.</summary>
    /// <returns>The URL, without credentials or query.</returns>
    public Uri BuildUrlWithoutPassword()
    {
        var url = Url;
        return new UriBuilder {
            Scheme = url.Scheme,
            Host = url.Host,
            Port = url.Port
        }.Uri;
    }
}