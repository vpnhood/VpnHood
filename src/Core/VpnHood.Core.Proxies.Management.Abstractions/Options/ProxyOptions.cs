namespace VpnHood.Core.Proxies.Management.Abstractions.Options;

/// <summary>
/// Provides the options of the VPN's connections through proxies.
/// </summary>
public class ProxyOptions
{
    /// <summary>Gets which proxies the VPN connects through. The default is <see cref="ProxyMode.None"/>.</summary>
    public ProxyMode Mode { get; init; } = ProxyMode.None;

    /// <summary>
    /// Gets the proxy used when <see cref="Mode"/> is <see cref="ProxyMode.Simple"/>; other modes ignore it. A managed
    /// list's endpoints are in the shared endpoint store.
    /// </summary>
    public ProxyEndPoint? ProxyEndPoint { get; init; }

    /// <summary>
    /// Gets a value indicating whether a managed connector clears the statuses of the stored endpoints when it starts
    /// or takes these options.
    /// </summary>
    public bool ResetStates { get; init; }

    /// <summary>Gets the options of a managed list's update from a URL.</summary>
    public ProxyAutoUpdateOptions AutoUpdateOptions { get; init; } = new();

    /// <summary>
    /// Gets a value indicating whether a managed connector's check of a proxy also completes a TLS handshake through
    /// it with <c>one.one.one.one</c>, whose certificate the system must trust. The default is <see langword="true"/>.
    /// </summary>
    public bool VerifyTls { get; init; } = true;
}
