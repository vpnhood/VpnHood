using System.Net;
using Ga4.Trackers;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;

namespace VpnHood.Client;

public static class ClientTrackerBuilder
{
    public static TrackEvent BuildConnectionSucceeded(string? serverLocation,
        bool isIpV6Supported, bool hasRedirected, IPEndPoint endPoint, string? adNetwork)
    {
        return BuildConnectionAttempt(connected: true, 
            serverLocation: serverLocation, 
            isIpV6Supported: isIpV6Supported,
            hasRedirected: hasRedirected, 
            endPoint: endPoint,
            adNetwork: adNetwork);
    }

    public static TrackEvent BuildConnectionFailed(string? serverLocation,
        bool isIpV6Supported, bool hasRedirected)
    {
        return BuildConnectionAttempt(connected: false,
            serverLocation: serverLocation,
            isIpV6Supported: isIpV6Supported,
            hasRedirected: hasRedirected,
            endPoint: null,
            adNetwork: null);
    }

    private static TrackEvent BuildConnectionAttempt(bool connected, string? serverLocation, 
        bool isIpV6Supported, bool hasRedirected, IPEndPoint? endPoint, string? adNetwork)
    {
        return new TrackEvent {
            EventName = "vh_connect_attempt",
            Parameters = new Dictionary<string, object> {
                { "server_location", serverLocation ?? string.Empty },
                { "connected", connected.ToString() },
                { "ipv6_supported", isIpV6Supported.ToString() },
                { "redirected", hasRedirected.ToString() },
                { "ad_network", adNetwork ?? string.Empty },
                { "endpoint", endPoint?.ToString() ?? string.Empty },
            }
        };
    }

    public static TrackEvent BuildEndPointStatus(IPEndPoint endPoint, bool available)
    {
        return new TrackEvent {
            EventName = "vh_endpoint_status",
            Parameters = new Dictionary<string, object> {
                { "ep", endPoint },
                { "ip_v6", endPoint.Address.IsV6() },
                { "available", available }
            }
        };
    }

    public static TrackEvent BuildUsage(Traffic usage, int requestCount, int connectionCount)
    {
        var trackEvent = new TrackEvent {
            EventName = "vh_usage",
            Parameters = new Dictionary<string, object> {
                { "traffic_total", Math.Round(usage.Total / 1_000_000d) },
                { "traffic_sent", Math.Round(usage.Sent / 1_000_000d) },
                { "traffic_received", Math.Round(usage.Received / 1_000_000d) },
                { "requests", requestCount },
                { "connections", connectionCount }
            }
        };

        return trackEvent;
    }
}