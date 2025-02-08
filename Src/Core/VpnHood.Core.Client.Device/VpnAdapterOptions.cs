﻿using System.Net;
using VpnHood.Core.Common.Net;

namespace VpnHood.Core.Client.Device;

public class VpnAdapterOptions
{
    public required IpNetwork? VirtualIpNetworkV4 { get; init; }
    public required IpNetwork? VirtualIpNetworkV6 { get; init; }
    public required IPAddress[] DnsServers { get; init; }
    public required IpNetwork[] IncludeNetworks { get; init; }
    public required string[]? ExcludeApps { get; init; }
    public required string[]? IncludeApps { get; init; }
    public required string? SessionName { get; init; }
    public int? Mtu { get; init; }
}