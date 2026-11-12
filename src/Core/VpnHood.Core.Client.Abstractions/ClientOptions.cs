using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Provides the options of a VPN connection, which the app writes for the VPN service, in its config folder, as
/// <see cref="VpnConfigFileName"/>.
/// </summary>
public class ClientOptions
{
    /// <summary>
    /// Gets or sets the addresses the device's tunnel captures, before the server's ranges and the local-network split
    /// narrow them; the session's DNS servers, where its DNS plan is in the tunnel, are added whatever this says, and the
    /// VPN server's own address is always taken out. The default is every address.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IpRange, IpRangeConverter>))]
    public IpRange[] IncludeIpRangesByDevice { get; set; } = [.. IpNetwork.All.ToIpRanges()];

    /// <summary>
    /// Gets or sets the DNS servers the person chose, or <see langword="null"/> for the automatic choice: the server's
    /// where the tunnel can carry them, else Google's.
    /// </summary>
    /// <remarks>
    /// Where <see cref="SplitDnsMode.IncludeAll"/> keeps DNS inside and the tunnel can carry no candidate, the connect
    /// fails.
    /// </remarks>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[]? DnsServers { get; set; }

    /// <summary>
    /// Gets or sets the client's version, which the servers get and may turn a client away by: the library's, whatever
    /// app runs it. The default is this assembly's version.
    /// </summary>
    // An app's own version tells the servers nothing: a fork's 1.0.12 is no older a client than ours
    // (TrackerClientVersion)
    [JsonConverter(typeof(VersionConverter))]
    public Version Version { get; set; } = typeof(ClientOptions).Assembly.GetName().Version ?? new Version();

    /// <summary>Gets or sets the name of the VPN adapter's interface.</summary>
    public required string AdapterName { get; set; }

    /// <summary>
    /// Gets or sets the app's ID, which the adapter writes on its interface where it can
    /// (<c>VpnAdapterSettings.AppId</c>), or <see langword="null"/>.
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>Gets or sets the access key to connect with, in its text form.</summary>
    public required string AccessKey { get; set; }

    /// <summary>Gets or sets the client's ID, the same for its every session.</summary>
    public required string ClientId { get; set; }

    /// <summary>Gets or sets the protocol of the tunnel's packet channels. The default is <see cref="ChannelProtocol.Tcp"/>.</summary>
    public ChannelProtocol ChannelProtocol { get; set; } = ChannelProtocol.Tcp;

    /// <summary>
    /// Gets or sets a value indicating whether the client ends each TCP connection on the device and has the server
    /// open a new one, rather than carrying its packets.
    /// </summary>
    /// <remarks>
    /// It is a preference, for each IP version: a server that cannot forward TCP turns it off, and one that does not
    /// carry that version's TCP as packets, or a domain filter, which needs it, turns it on.
    /// </remarks>
    public bool UseTcpForwarding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client drops the QUIC bound for the tunnel, UDP to ports 80 and 443,
    /// so apps turn to TCP; it applies only while the client forwards IPv4 TCP, and then to either IP version.
    /// </summary>
    public bool DropQuic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client drops the UDP bound for the tunnel, but to DNS's ports; it
    /// applies only while the client forwards IPv4 TCP, and then to either IP version.
    /// </summary>
    public bool DropUdp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client disposes its VPN adapter with itself. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool AutoDisposeVpnAdapter { get; set; } = true;

    /// <summary>
    /// Gets or sets the transport's tuning, which the client and its sessions hold by reference.
    /// </summary>
    public ClientTransportOptions Transport { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the local networks, with multicast and broadcast, stay out of the VPN,
    /// so their devices are reached directly; a DNS server of the session's that the tunnel carries stays in.
    /// </summary>
    public bool SplitLocalNetwork { get; set; }

    /// <summary>Gets or sets where DNS may go. The default is <see cref="SplitDnsMode.IncludeAll"/>.</summary>
    public SplitDnsMode SplitDnsMode { get; set; }

    /// <summary>
    /// Gets or sets what happens to a destination the server does not route. The default is
    /// <see cref="SplitUnsupportedIpMode.Exclude"/>.
    /// </summary>
    public SplitUnsupportedIpMode UnroutedIpMode { get; set; }

    /// <summary>
    /// Gets or sets what happens to the IPv6 in the device's ranges when the server carries no IPv6 at all. The default
    /// is <see cref="SplitUnsupportedIpMode.Block"/>, so it does not go around the tunnel, where a WebRTC or STUN probe
    /// could learn the device's real address; IPv6 the person's own splits leave out still goes out directly.
    /// </summary>
    /// <remarks>
    /// <see cref="UnroutedIpMode"/>'s <see cref="SplitUnsupportedIpMode.Block"/> blocks IPv6 too, whatever this says.
    /// </remarks>
    public SplitUnsupportedIpMode UnsupportedIpV6Mode { get; set; } = SplitUnsupportedIpMode.Block;

    /// <summary>Gets or sets the user agent the servers get. The default is the OS's version.</summary>
    public string UserAgent { get; set; } = Environment.OSVersion.ToString();

    /// <summary>
    /// Gets or sets the shortest age limit of a TCP or QUIC packet channel. Each channel's limit is drawn at random from
    /// this up to, but not including, <see cref="MaxPacketChannelTimespan"/>, both cut to whole seconds, or is that
    /// value where the two are then equal; a channel past its limit closes gracefully at its next check, for the client
    /// to open another. The UDP channel has no limit. The default is 5 minutes.
    /// </summary>
    public TimeSpan MinPacketChannelTimespan { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the longest age limit of a TCP or QUIC packet channel; an infinite one gives channels no age limit,
    /// though they still close for other causes. The default is 10 minutes.
    /// </summary>
    public TimeSpan MaxPacketChannelTimespan { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets a value indicating whether the person lets their use be reported anonymously: the session to the
    /// server's analytics, which the server is told of and the client reports to itself where the server names a GA4
    /// measurement ID; and the events of the app's trackers the VPN service makes again
    /// (<see cref="TrackerFactoryInfos"/>), which send nothing without it. The default is <see langword="true"/>.
    /// </summary>
    public bool AllowAnonymousTracker { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the app's trackers get the availability events of the server endpoints
    /// the client checks while it finds one that answers; the event of a connect that succeeded carries its endpoint
    /// whatever this says.
    /// </summary>
    public bool AllowEndPointTracker { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client keeps its link to the server, a TCP connection or a QUIC
    /// stream, once a request or a forwarded TCP connection over it ended cleanly, for the next one; a packet channel's
    /// is never kept, and a QUIC connection carries further streams either way. The default is <see langword="true"/>.
    /// </summary>
    public bool AllowChannelReuse { get; set; } = true;

    /// <summary>
    /// Gets or sets the server location to connect to, as <see cref="ServerLocationInfo.Parse"/> reads one, or
    /// <see langword="null"/> for the automatic one.
    /// </summary>
    public string? ServerLocation { get; set; }

    /// <summary>Gets or sets the plan to connect with. The default is <see cref="ConnectPlanId.Normal"/>.</summary>
    public ConnectPlanId PlanId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client reads and logs the domain names of the device's flows even
    /// where no domain rule needs them.
    /// </summary>
    public bool ForceLogSni { get; set; }

    /// <summary>Gets or sets the premium code to connect with, or <see langword="null"/>.</summary>
    public string? AccessCode { get; set; }

    /// <summary>Gets or sets the name the VPN adapter gives its session where the OS shows one, or <see langword="null"/>.</summary>
    public string? SessionName { get; set; }

    /// <summary>Gets or sets the apps to keep out of the tunnel, by ID, or <see langword="null"/>.</summary>
    public string[]? ExcludeApps { get; set; }

    /// <summary>Gets or sets the only apps to let into the tunnel, by ID, or <see langword="null"/> for every app.</summary>
    public string[]? IncludeApps { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the VPN service connects with a null adapter, which captures nothing,
    /// for tests.
    /// </summary>
    public bool UseNullCapture { get; set; }

    /// <summary>Gets or sets the options of the VPN service's log.</summary>
    public LogServiceOptions LogServiceOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets the app's tracker factories, which the VPN service makes again in its own process. The default is
    /// none.
    /// </summary>
    public IReadOnlyList<TrackerFactoryInfo> TrackerFactoryInfos { get; set; } = [];

    /// <summary>
    /// Gets or sets the user agent the trackers send: <see cref="UserAgent"/> with the device's model too. The default
    /// is the OS's version.
    /// </summary>
    public string TrackerUserAgent { get; set; } = Environment.OSVersion.ToString();

    /// <summary>
    /// Gets or sets the version the trackers send: the app's own, where the servers get the client's
    /// (<see cref="Version"/>). The default is this assembly's version.
    /// </summary>
    [JsonConverter(typeof(VersionConverter))]
    public Version TrackerClientVersion { get; set; } = typeof(ClientOptions).Assembly.GetName().Version ?? new Version();

    /// <summary>
    /// Gets or sets text of debug switches the developer gave, such as <c>/disable-WebSocket</c>, or
    /// <see langword="null"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? DebugData1 { get; set; }

    /// <summary>Gets or sets a second text the developer gave, or <see langword="null"/>; nothing in the library reads it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? DebugData2 { get; set; }

    /// <summary>
    /// Gets or sets how the client orders the server token's addresses. The default is
    /// <see cref="EndPointStrategy.Auto"/>, the token's own strategy.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public EndPointStrategy EndPointStrategy { get; set; }

    /// <summary>
    /// Gets or sets the server endpoints to connect to in place of the server token's, or <see langword="null"/>; while
    /// they are set, the client asks the server not to redirect it.
    /// </summary>
    /// <remarks>
    /// After the VPN service ended without closing, as in a crash, the server it was connected to under the same access
    /// key is tried first, before these.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? CustomServerEndpoints { get; set; }

    /// <summary>Gets or sets a value indicating whether the app lets the VPN connect as always on.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowAlwaysOn { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this connection is the always-on one, which a desktop's app makes
    /// itself; Android's system says so to the service instead. Either is refused without <see cref="AllowAlwaysOn"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAlwaysOn { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the kill switch is on, where the device has one: the app sets it only
    /// where the person turned it on and the plan allows it, so the service obeys it as it is.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseKillSwitch { get; set; }

    /// <summary>Gets or sets the user's review of the app to send with the connection, or <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public UserReview? UserReview { get; set; }

    /// <summary>A sample access key, of VpnHood's public servers, for tests and samples.</summary>
    // ReSharper disable StringLiteralTypo
    public const string SampleAccessKey =
        "vh:eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBTYW1wbGUiLCJzaWQiOiIxMzAwIiwidGlkIjoiYTM0Mjk4ZDktY2YwYi00MGEwLWI5NmMtZGJhYjYzMWQ2MGVjIiwiaWF0IjoiMjAyNS0wMS0zMVQxMDoxNDozMi4xMTQ0NjI5WiIsInNlYyI6Im9wcTJ6M0M0ak9rdHNodXl3c0VKNXc9PSIsInNlciI6eyJjdCI6IjIwMjUtMDEtMzBUMDg6NTM6MzhaIiwiaG5hbWUiOiJkb3dubG9hZC5taWNyb3NvZnQuY29tIiwiaHBvcnQiOjAsImlzdiI6ZmFsc2UsInNlYyI6InZhQnFVOVJDM1FIYVc0eEY1aWJZRnc9PSIsImNoIjoiOURQUTYvcmRySmFybU90NmQyVHRRMmE2cjlzPSIsInVybCI6Imh0dHBzOi8vZ2l0bGFiLmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvLS9yYXcvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0IiwidXJscyI6WyJodHRwczovL2dpdGxhYi5jb20vdnBuaG9vZC9WcG5Ib29kLkZhcm1LZXlzLy0vcmF3L21haW4vR2xvYmFsX0Zhcm1fZW5jcnlwdGVkX3Rva2VuLnR4dCIsImh0dHBzOi8vYml0YnVja2V0Lm9yZy92cG5ob29kL3Zwbmhvb2QuZmFybWtleXMvcmF3L21haW4vR2xvYmFsX0Zhcm1fZW5jcnlwdGVkX3Rva2VuLnR4dCIsImh0dHBzOi8vcmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0Il0sImVwIjpbIjUxLjgxLjgxLjI1MDo0NDMiLCJbMjYwNDoyZGMwOjEwMToyMDA6OjkzZV06NDQzIiwiNTEuODEuNTUuMTIzOjQ0MyIsIjUxLjgxLjE3MS4xNzE6NDQzIiwiMTUuMjA0Ljg3LjkwOjQ0MyIsIjE1LjIwNC4yMDkuODg6NDQzIiwiMTM1LjE0OC4zOS4yMjI6NDQzIiwiODIuMTgwLjE0Ny4xOTQ6NDQzIiwiMTk0LjE2NC4xMjYuNzA6NDQzIiwiWzJhMDA6ZGEwMDpmNDBkOjMzMDA6OjFdOjQ0MyIsIjUxLjgxLjIxMC4xNjQ6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjo1Y2VdOjQ0MyIsIjUxLjgxLjE3MS4xODM6NDQzIiwiNTEuNzkuNzMuMjQwOjQ0MyIsIlsyNjA3OjUzMDA6MjA1OjIwMDo6NTNiMF06NDQzIiwiMTkyLjk5LjE3Ny4yNDQ6NDQzIiwiNTEuODEuNTUuMTIyOjQ0MyIsIjUxLjgxLjY5LjE1NDo0NDMiLCI1Ny4xMjguMjAwLjEzOTo0NDMiLCJbMjAwMTo0MWQwOjYwMToxMTAwOjoxM2E0XTo0NDMiLCI1MS44MS4xNzEuMTcwOjQ0MyIsIjE5NC4yNDYuMTE0LjIyOjQ0MyIsIjUuMjUwLjE5MC44OjQ0MyIsIlsyMDAxOmJhMDoyMmQ6ZWQwMDo6MV06NDQzIl0sImxvYyI6WyJBTS9ZZXJldmFuIiwiQVUvTmV3IFNvdXRoIFdhbGVzIiwiQlIvU2FvIFBhdWxvIiwiQ0EvUXVlYmVjIiwiRlIvSGF1dHMtZGUtRnJhbmNlIiwiREUvQmVybGluIiwiSEsvSG9uZyBLb25nIiwiSU4vTWFoYXJhc2h0cmEiLCJKUC9Ub2t5byIsIktaL0FsbWF0eSIsIk1YL1F1ZXJldGFybyIsIlBML01hem92aWEiLCJSVS9Nb3Njb3ciLCJTRy9TaW5nYXBvcmUiLCJaQS9HYXV0ZW5nIiwiRVMvTWFkcmlkIiwiVFIvQnVyc2EgUHJvdmluY2UiLCJBRS9EdWJhaSIsIkdCL0VuZ2xhbmQgIiwiVVMvT3JlZ29uICIsIlVTL1ZpcmdpbmlhIl0sImxvYzIiOlsiQU0vWWVyZXZhbiIsIkFVL05ldyBTb3V0aCBXYWxlcyIsIkJSL1NhbyBQYXVsbyIsIkNBL1F1ZWJlYyIsIkZSL0hhdXRzLWRlLUZyYW5jZSIsIkRFL0JlcmxpbiIsIkhLL0hvbmcgS29uZyIsIklOL01haGFyYXNodHJhIiwiSlAvVG9reW8iLCJLWi9BbG1hdHkiLCJNWC9RdWVyZXRhcm8iLCJQTC9NYXpvdmlhIiwiUlUvTW9zY293IiwiU0cvU2luZ2Fwb3JlIiwiWkEvR2F1dGVuZyIsIkVTL01hZHJpZCIsIlRSL0J1cnNhIFByb3ZpbmNlIiwiQUUvRHViYWkiLCJHQi9FbmdsYW5kIFsjdW5ibG9ja2FibGVdIiwiVVMvT3JlZ29uIFt+I3VuYmxvY2thYmxlXSIsIlVTL1ZpcmdpbmlhIl19LCJ0YWdzIjpbXSwiaXNwdWIiOnRydWUsImNwb2xzIjpbeyJjY3MiOlsiKiJdLCJuIjoxMCwicGJ0IjoxMCwicGJyIjo2MCwicGJwIjp0cnVlLCJwYmMiOnRydWV9XX0=";
    // ReSharper restore StringLiteralTypo

    /// <summary>The name of the file of the connection's options in the VPN service's config folder: <c>vpn.config</c>.</summary>
    public const string VpnConfigFileName = "vpn.config";

    /// <summary>The name of the file of the connection's status in the VPN service's config folder: <c>vpn.status</c>.</summary>
    public const string VpnStatusFileName = "vpn.status";

    /// <summary>The name of the VPN service's log file in its config folder: <c>vpn.log</c>.</summary>
    public const string VpnLogFileName = "vpn.log";
}
