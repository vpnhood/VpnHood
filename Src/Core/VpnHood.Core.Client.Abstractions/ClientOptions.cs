using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Client.Abstractions;

public class ClientOptions
{
    public static ClientOptions Default { get; } = new() { ClientId = string.Empty, AccessKey = SampleAccessKey, AppName = "VpnHoodEngine" };

    [JsonConverter(typeof(ArrayConverter<IpRange, IpRangeConverter>))]
    public IpRange[] IncludeIpRanges { get; set; } = IpNetwork.All.ToIpRanges().ToArray();

    [JsonConverter(typeof(ArrayConverter<IpRange, IpRangeConverter>))]
    public IpRange[] VpnAdapterIncludeIpRanges { get; set; } = IpNetwork.All.ToIpRanges().ToArray();

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[]? DnsServers { get; set; }

    // a never used IPv4 that must be outside the local network
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress TcpProxyCatcherAddressIpV4 { get; set; } = IPAddress.Parse("11.0.0.0");

    // a never used IPv6 ip that must be outside the machine
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress TcpProxyCatcherAddressIpV6 { get; set; } = IPAddress.Parse("2000::");

    [JsonConverter(typeof(VersionConverter))]
    public Version Version { get; set; } = typeof(ClientOptions).Assembly.GetName().Version;
    public required string AppName { get; set; }
    public required string AccessKey { get; set; }
    public required string ClientId { get; set; }
    public bool AutoDisposeVpnAdapter { get; set; } = true;
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromDays(3);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromSeconds(60); // connect timeout before pause
    public TimeSpan AutoWaitTimeout { get; set; } = TimeSpan.FromSeconds(30); // auto resume after pause
    public bool UseUdpChannel { get; set; }
    public bool IncludeLocalNetwork { get; set; }
    public int MaxDatagramChannelCount { get; set; } = 1; //todo: must be 4
    public string UserAgent { get; set; } = Environment.OSVersion.ToString();
    public TimeSpan MinTcpDatagramTimespan { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaxTcpDatagramTimespan { get; set; } = TimeSpan.FromMinutes(10);
    public bool AllowAnonymousTracker { get; set; } = true;
    public bool AllowEndPointTracker { get; set; }
    public bool AllowTcpReuse { get; set; } = true;
    public bool UseTcpOverTun { get; set; }
    public bool DropUdp { get; set; }
    public bool DropQuic { get; set; }
    public string? ServerLocation { get; set; }
    public ConnectPlanId PlanId { get; set; }
    public DomainFilter DomainFilter { get; set; } = new();
    public bool ForceLogSni { get; set; }
    public TimeSpan ServerQueryTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool AllowRewardedAd { get; set; }
    public TimeSpan CanExtendByRewardedAdThreshold { get; set; } = TimeSpan.FromMinutes(5);
    public string? AccessCode { get; set; }
    public string? SessionName { get; set; }
    public string[]? ExcludeApps { get; set; }
    public string[]? IncludeApps { get; set; }
    public bool UseNullCapture { get; set; }
    public LogServiceOptions LogServiceOptions { get; set; } = new();
    public string? Ga4MeasurementId { get; set; }
    public string? TrackerFactoryAssemblyQualifiedName { get; set; }
    public string? DebugData1 { get; set; }
    public string? DebugData2 { get; set; }

    // ReSharper disable StringLiteralTypo
    public const string SampleAccessKey =
        "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBTYW1wbGUiLCJzaWQiOiIxMzAwIiwidGlkIjoiYTM0Mjk4ZDktY2YwYi00MGEwLWI5NmMtZGJhYjYzMWQ2MGVjIiwiaWF0IjoiMjAyNS0wMS0zMVQxMDoxNDozMi4xMTQ0NjI5WiIsInNlYyI6Im9wcTJ6M0M0ak9rdHNodXl3c0VKNXc9PSIsInNlciI6eyJjdCI6IjIwMjUtMDEtMzBUMDg6NTM6MzhaIiwiaG5hbWUiOiJkb3dubG9hZC5taWNyb3NvZnQuY29tIiwiaHBvcnQiOjAsImlzdiI6ZmFsc2UsInNlYyI6InZhQnFVOVJDM1FIYVc0eEY1aWJZRnc9PSIsImNoIjoiOURQUTYvcmRySmFybU90NmQyVHRRMmE2cjlzPSIsInVybCI6Imh0dHBzOi8vZ2l0bGFiLmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvLS9yYXcvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0IiwidXJscyI6WyJodHRwczovL2dpdGxhYi5jb20vdnBuaG9vZC9WcG5Ib29kLkZhcm1LZXlzLy0vcmF3L21haW4vR2xvYmFsX0Zhcm1fZW5jcnlwdGVkX3Rva2VuLnR4dCIsImh0dHBzOi8vYml0YnVja2V0Lm9yZy92cG5ob29kL3Zwbmhvb2QuZmFybWtleXMvcmF3L21haW4vR2xvYmFsX0Zhcm1fZW5jcnlwdGVkX3Rva2VuLnR4dCIsImh0dHBzOi8vcmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0Il0sImVwIjpbIjUxLjgxLjgxLjI1MDo0NDMiLCJbMjYwNDoyZGMwOjEwMToyMDA6OjkzZV06NDQzIiwiNTEuODEuNTUuMTIzOjQ0MyIsIjUxLjgxLjE3MS4xNzE6NDQzIiwiMTUuMjA0Ljg3LjkwOjQ0MyIsIjE1LjIwNC4yMDkuODg6NDQzIiwiMTM1LjE0OC4zOS4yMjI6NDQzIiwiODIuMTgwLjE0Ny4xOTQ6NDQzIiwiMTk0LjE2NC4xMjYuNzA6NDQzIiwiWzJhMDA6ZGEwMDpmNDBkOjMzMDA6OjFdOjQ0MyIsIjUxLjgxLjIxMC4xNjQ6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjo1Y2VdOjQ0MyIsIjUxLjgxLjE3MS4xODM6NDQzIiwiNTEuNzkuNzMuMjQwOjQ0MyIsIlsyNjA3OjUzMDA6MjA1OjIwMDo6NTNiMF06NDQzIiwiMTkyLjk5LjE3Ny4yNDQ6NDQzIiwiNTEuODEuNTUuMTIyOjQ0MyIsIjUxLjgxLjY5LjE1NDo0NDMiLCI1Ny4xMjguMjAwLjEzOTo0NDMiLCJbMjAwMTo0MWQwOjYwMToxMTAwOjoxM2E0XTo0NDMiLCI1MS44MS4xNzEuMTcwOjQ0MyIsIjE5NC4yNDYuMTE0LjIyOjQ0MyIsIjUuMjUwLjE5MC44OjQ0MyIsIlsyMDAxOmJhMDoyMmQ6ZWQwMDo6MV06NDQzIl0sImxvYyI6WyJBTS9ZZXJldmFuIiwiQVUvTmV3IFNvdXRoIFdhbGVzIiwiQlIvU2FvIFBhdWxvIiwiQ0EvUXVlYmVjIiwiRlIvSGF1dHMtZGUtRnJhbmNlIiwiREUvQmVybGluIiwiSEsvSG9uZyBLb25nIiwiSU4vTWFoYXJhc2h0cmEiLCJKUC9Ub2t5byIsIktaL0FsbWF0eSIsIk1YL1F1ZXJldGFybyIsIlBML01hem92aWEiLCJSVS9Nb3Njb3ciLCJTRy9TaW5nYXBvcmUiLCJaQS9HYXV0ZW5nIiwiRVMvTWFkcmlkIiwiVFIvQnVyc2EgUHJvdmluY2UiLCJBRS9EdWJhaSIsIkdCL0VuZ2xhbmQgIiwiVVMvT3JlZ29uICIsIlVTL1ZpcmdpbmlhIl0sImxvYzIiOlsiQU0vWWVyZXZhbiIsIkFVL05ldyBTb3V0aCBXYWxlcyIsIkJSL1NhbyBQYXVsbyIsIkNBL1F1ZWJlYyIsIkZSL0hhdXRzLWRlLUZyYW5jZSIsIkRFL0JlcmxpbiIsIkhLL0hvbmcgS29uZyIsIklOL01haGFyYXNodHJhIiwiSlAvVG9reW8iLCJLWi9BbG1hdHkiLCJNWC9RdWVyZXRhcm8iLCJQTC9NYXpvdmlhIiwiUlUvTW9zY293IiwiU0cvU2luZ2Fwb3JlIiwiWkEvR2F1dGVuZyIsIkVTL01hZHJpZCIsIlRSL0J1cnNhIFByb3ZpbmNlIiwiQUUvRHViYWkiLCJHQi9FbmdsYW5kIFsjdW5ibG9ja2FibGVdIiwiVVMvT3JlZ29uIFt+I3VuYmxvY2thYmxlXSIsIlVTL1ZpcmdpbmlhIl19LCJ0YWdzIjpbXSwiaXNwdWIiOnRydWUsImNwb2xzIjpbeyJjY3MiOlsiKiJdLCJuIjoxMCwicGJ0IjoxMCwicGJyIjo2MCwicGJwIjp0cnVlLCJwYmMiOnRydWV9XX0=";
    // ReSharper restore StringLiteralTypo
    public const string VpnConfigFileName = "vpn.config";
    public const string VpnStatusFileName = "vpn.status";
    public const string VpnLogFileName = "vpn.log";
}