using System.Security.Cryptography;
using System.Text;

namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPoint
{
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

    public bool IsEnabled { get; set; } = true;
    public required ProxyProtocol Protocol { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    public Uri Url =>
        new UriBuilder {
            Scheme = Protocol.ToString().ToLower(),
            Host = Host,
            Port = Port,
            UserName = Username,
            Password = Password,
            Query = IsEnabled ? "enabled=1" : null
        }.Uri;

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