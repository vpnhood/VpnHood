using System.Text.Json.Serialization;

namespace VpnHood.Core.Server.Access.Configurations;

/// <summary>
/// Represents what the server's track log records of its sessions and their connections.
/// </summary>
/// <remarks>
/// A setting left <see langword="null"/> takes its default, which the property of its name ending in <c>Value</c>
/// gives. The track log is on where any of the client's IP, the source port, or the destination's IP or port is
/// recorded (<see cref="IsEnabled"/>). It records what the server forwards itself, not what its VPN adapter carries.
/// </remarks>
public class TrackingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the session and track logs record the client's IP as it is, or
    /// <see langword="null"/> for <see langword="false"/>.
    /// </summary>
    public bool? TrackClientIp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the track log records the source port of each connection the server
    /// forwards itself, or <see langword="null"/> for <see langword="false"/>.
    /// </summary>
    public bool? TrackLocalPort { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the track log records the destination address of each connection the
    /// server forwards itself, as a token that tells one from another, or <see langword="null"/> for
    /// <see langword="false"/>.
    /// </summary>
    public bool? TrackDestinationIp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the track log records the destination port of each connection the
    /// server forwards itself, or <see langword="null"/> for <see langword="false"/>.
    /// </summary>
    public bool? TrackDestinationPort { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the track log records UDP, or <see langword="null"/> for
    /// <see langword="true"/>.
    /// </summary>
    public bool? TrackUdp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the track log records TCP, or <see langword="null"/> for
    /// <see langword="true"/>.
    /// </summary>
    public bool? TrackTcp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the track log records ICMP, or <see langword="null"/> for
    /// <see langword="true"/>.
    /// </summary>
    public bool? TrackIcmp { get; set; }

    /// <summary>
    /// Gets a value indicating whether the track log is on: any of the client's IP, the source port, or the
    /// destination's IP or port is recorded.
    /// </summary>
    [JsonIgnore]
    public bool IsEnabled =>
        TrackClientIpValue || TrackLocalPortValue || TrackDestinationIpValue || TrackDestinationPortValue;

    /// <summary>Gets <see cref="TrackClientIp"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool TrackClientIpValue => TrackClientIp ?? false;

    /// <summary>Gets <see cref="TrackLocalPort"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool TrackLocalPortValue => TrackLocalPort ?? false;

    /// <summary>Gets <see cref="TrackDestinationIp"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool TrackDestinationIpValue => TrackDestinationIp ?? false;

    /// <summary>Gets <see cref="TrackDestinationPort"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool TrackDestinationPortValue => TrackDestinationPort ?? false;

    /// <summary>Gets <see cref="TrackUdp"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool TrackUdpValue => TrackUdp ?? true;

    /// <summary>Gets <see cref="TrackTcp"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool TrackTcpValue => TrackTcp ?? true;

    /// <summary>Gets <see cref="TrackIcmp"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool TrackIcmpValue => TrackIcmp ?? true;

    /// <summary>Copies over these options each one another set sets.</summary>
    /// <param name="obj">The options that win where they are set.</param>
    public void Merge(TrackingOptions obj)
    {
        if (obj.TrackClientIp != null) TrackClientIp = obj.TrackClientIp;
        if (obj.TrackLocalPort != null) TrackLocalPort = obj.TrackLocalPort;
        if (obj.TrackDestinationIp != null) TrackDestinationIp = obj.TrackDestinationIp;
        if (obj.TrackDestinationPort != null) TrackDestinationPort = obj.TrackDestinationPort;
        if (obj.TrackUdp != null) TrackUdp = obj.TrackUdp;
        if (obj.TrackTcp != null) TrackTcp = obj.TrackTcp;
        if (obj.TrackIcmp != null) TrackIcmp = obj.TrackIcmp;
    }

    /// <summary>Sets each option left <see langword="null"/> to its default.</summary>
    public void ApplyDefaults()
    {
        TrackClientIp = TrackClientIpValue;
        TrackLocalPort = TrackLocalPortValue;
        TrackDestinationIp = TrackDestinationIpValue;
        TrackDestinationPort = TrackDestinationPortValue;
        TrackUdp = TrackUdpValue;
        TrackTcp = TrackTcpValue;
        TrackIcmp = TrackIcmpValue;
    }
}