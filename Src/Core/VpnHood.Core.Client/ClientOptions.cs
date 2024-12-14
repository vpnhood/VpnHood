using System.Net;
using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Tunneling.DomainFiltering;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client;

public class ClientOptions
{
    public static ClientOptions Default { get; } = new();

    /// <summary>
    ///     a never used IPv4 that must be outside the local network
    /// </summary>
    public IPAddress TcpProxyCatcherAddressIpV4 { get; set; } = IPAddress.Parse("11.0.0.0");

    /// <summary>
    ///     a never used IPv6 ip that must be outside the machine
    /// </summary>
    public IPAddress TcpProxyCatcherAddressIpV6 { get; set; } = IPAddress.Parse("2000::");

    public IPAddress[]? DnsServers { get; set; }
    public bool AutoDisposePacketCapture { get; set; } = true;
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromDays(3);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromSeconds(60); // connect timeout before pause
    public TimeSpan AutoWaitTimeout { get; set; } = TimeSpan.FromSeconds(30); // auto resume after pause
    public Version Version { get; set; } = typeof(ClientOptions).Assembly.GetName().Version;
    public bool UseUdpChannel { get; set; }
    public bool IncludeLocalNetwork { get; set; }
    public IAdService? AdService { get; set; }
    public IpRangeOrderedList IncludeIpRanges { get; set; } = new(IpNetwork.All.ToIpRanges());
    public IpRangeOrderedList PacketCaptureIncludeIpRanges { get; set; } = new(IpNetwork.All.ToIpRanges());
    public SocketFactory SocketFactory { get; set; } = new();
    public int MaxDatagramChannelCount { get; set; } = 4;
    public string UserAgent { get; set; } = Environment.OSVersion.ToString();
    public TimeSpan MinTcpDatagramTimespan { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaxTcpDatagramTimespan { get; set; } = TimeSpan.FromMinutes(10);
    public bool AllowAnonymousTracker { get; set; } = true;
    public bool AllowEndPointTracker { get; set; }
    public bool DropUdp { get; set; }
    public bool DropQuic { get; set; }
    public string? ServerLocation { get; set; }
    public ConnectPlanId PlanId { get; set; }
    public DomainFilter DomainFilter { get; set; } = new();
    public bool ForceLogSni { get; set; }
    public TimeSpan ServerQueryTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public ITracker? Tracker { get; set; }
    public TimeSpan CanExtendByRewardedAdThreshold { get; set; } = TimeSpan.FromMinutes(5);

    // ReSharper disable StringLiteralTypo
    public const string SampleAccessKey =
        "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBTYW1wbGUiLCJzaWQiOiIxMzAwIiwidGlkIjoiYTM0Mjk4ZDktY2YwYi00MGEwLWI5NmMtZGJhYjYzMWQ2MGVjIiwiaWF0IjoiMjAyNC0wNi0xNFQyMjozMjo1NS44OTQ5ODAyWiIsInNlYyI6Im9wcTJ6M0M0ak9rdHNodXl3c0VKNXc9PSIsInNlciI6eyJjdCI6IjIwMjQtMDYtMDVUMDQ6MTU6MzZaIiwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6MCwiaXN2IjpmYWxzZSwic2VjIjoidmFCcVU5UkMzUUhhVzR4RjVpYllGdz09IiwiY2giOiIzZ1hPSGU1ZWN1aUM5cStzYk83aGxMb2tRYkE9IiwidXJsIjoiaHR0cHM6Ly9yYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tL3Zwbmhvb2QvVnBuSG9vZC5GYXJtS2V5cy9tYWluL0ZyZWVfZW5jcnlwdGVkX3Rva2VuLnR4dCIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjo1Y2VdOjQ0MyJdLCJsb2MiOlsiVVMvT3JlZ29uIiwiVVMvVmlyZ2luaWEiXX19";
    // ReSharper restore StringLiteralTypo
}