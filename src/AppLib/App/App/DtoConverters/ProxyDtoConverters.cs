using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.Settings;
using CoreOptions = VpnHood.Core.Proxies.Management.Abstractions.Options;
using CoreProxy = VpnHood.Core.Proxies.Management.Abstractions;

namespace VpnHood.AppLib.DtoConverters;

// The app's proxy shapes and the engine's, in both directions. The engine's ProxyEndPoint computes
// its own Id and Url and rates its own quality; the contract carries those as values, because a
// contract holds no behaviour and a second implementation of the rating rule in a UI would drift
// from the one the engine actually routes by. Every enum maps with no default arm on purpose: a
// protocol or a grade added to the engine breaks this file rather than reaching a UI as Unknown.
public static class ProxyDtoConverters
{
    public static ProxyEndPoint ToAppDto(this CoreProxy.ProxyEndPoint endPoint)
    {
        return new ProxyEndPoint {
            Id = endPoint.Id,
            IsEnabled = endPoint.IsEnabled,
            Protocol = endPoint.Protocol.ToAppDto(),
            Host = endPoint.Host,
            Port = endPoint.Port,
            Username = endPoint.Username,
            Password = endPoint.Password,
            Url = endPoint.Url.ToString()
        };
    }

    // Id and Url are not read back: they are the engine's to derive from the address.
    public static CoreProxy.ProxyEndPoint ToEngine(this ProxyEndPoint endPoint)
    {
        return new CoreProxy.ProxyEndPoint {
            IsEnabled = endPoint.IsEnabled,
            Protocol = endPoint.Protocol.ToEngine(),
            Host = endPoint.Host,
            Port = endPoint.Port,
            Username = endPoint.Username,
            Password = endPoint.Password
        };
    }

    public static ProxyEndPointStatus ToAppDto(this CoreProxy.ProxyEndPointStatus status)
    {
        return new ProxyEndPointStatus {
            Penalty = status.Penalty,
            SucceededCount = status.SucceededCount,
            FailedCount = status.FailedCount,
            Latency = status.Latency,
            LastSucceeded = status.LastSucceeded,
            LastFailed = status.LastFailed,
            ErrorMessage = status.ErrorMessage,
            QueuePosition = status.QueuePosition,
            Quality = status.Quality.ToAppDto()
        };
    }

    public static ProxySessionStatus ToAppDto(this CoreProxy.ProxySessionStatus status)
    {
        return new ProxySessionStatus {
            SucceededCount = status.SucceededCount,
            FailedCount = status.FailedCount,
            Latency = status.Latency,
            LastSucceeded = status.LastSucceeded,
            LastFailed = status.LastFailed,
            ErrorMessage = status.ErrorMessage
        };
    }

    public static CoreProxy.ProxyEndPointDefaults ToEngine(this ProxyEndPointDefaults defaults)
    {
        return new CoreProxy.ProxyEndPointDefaults {
            IsEnabled = defaults.IsEnabled,
            Protocol = defaults.Protocol?.ToEngine(),
            Port = defaults.Port,
            Username = defaults.Username,
            Password = defaults.Password
        };
    }

    public static CoreOptions.ProxyAutoUpdateOptions ToEngine(this ProxyAutoUpdateOptions options)
    {
        return new CoreOptions.ProxyAutoUpdateOptions {
            Url = options.Url,
            Interval = options.Interval,
            MaxPenalty = options.MaxPenalty,
            MaxItemCount = options.MaxItemCount,
            RemoveDuplicateIps = options.RemoveDuplicateIps
        };
    }

    public static ProxyProtocol ToAppDto(this CoreProxy.ProxyProtocol protocol)
    {
        return protocol switch {
            CoreProxy.ProxyProtocol.Socks4 => ProxyProtocol.Socks4,
            CoreProxy.ProxyProtocol.Socks5 => ProxyProtocol.Socks5,
            CoreProxy.ProxyProtocol.Http => ProxyProtocol.Http,
            CoreProxy.ProxyProtocol.Https => ProxyProtocol.Https
        };
    }

    public static CoreProxy.ProxyProtocol ToEngine(this ProxyProtocol protocol)
    {
        return protocol switch {
            ProxyProtocol.Socks4 => CoreProxy.ProxyProtocol.Socks4,
            ProxyProtocol.Socks5 => CoreProxy.ProxyProtocol.Socks5,
            ProxyProtocol.Http => CoreProxy.ProxyProtocol.Http,
            ProxyProtocol.Https => CoreProxy.ProxyProtocol.Https
        };
    }

    public static StatusQuality ToAppDto(this CoreProxy.StatusQuality quality)
    {
        return quality switch {
            CoreProxy.StatusQuality.Unknown => StatusQuality.Unknown,
            CoreProxy.StatusQuality.Excellent => StatusQuality.Excellent,
            CoreProxy.StatusQuality.Good => StatusQuality.Good,
            CoreProxy.StatusQuality.Fair => StatusQuality.Fair,
            CoreProxy.StatusQuality.Poor => StatusQuality.Poor,
            CoreProxy.StatusQuality.VeryPoor => StatusQuality.VeryPoor,
            CoreProxy.StatusQuality.Failed => StatusQuality.Failed
        };
    }
}
