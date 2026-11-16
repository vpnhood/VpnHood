using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Provides a stage of an IP filter pipe that decides by sets held in memory: blocked, excluded and included address
/// ranges, and blocked TCP and UDP ports.
/// </summary>
/// <remarks>
/// A destination in the blocked ranges or on a blocked port is blocked, whatever the next stage would say. For any
/// other, the next stage decides first, and its verdict stands unless it is <see cref="FilterAction.Default"/>. Then
/// a destination in the excluded ranges is excluded; and, when the included ranges are not empty, one outside them
/// is excluded too. The rest get <see cref="FilterAction.Default"/>: the stage's own sets never give
/// <see cref="FilterAction.Include"/>. Setting a set raises <see cref="Changed"/>.
/// </remarks>
public class StaticIpFilter : IIpFilter
{
    private readonly IIpFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;

    // arrays, as a loop over an interface would take an enumerator for each packet
    private PortRange[] _blockedTcpPorts = [];
    private PortRange[] _blockedUdpPorts = [];

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticIpFilter"/> class, with empty sets.
    /// </summary>
    /// <param name="nextFilter">
    /// The stage that decides what this stage's blocks let through, before its other sets, or <see langword="null"/>.
    /// </param>
    /// <param name="autoDisposeNextFilter">
    /// <see langword="true"/> to dispose <paramref name="nextFilter"/> with this stage.
    /// </param>
    public StaticIpFilter(IIpFilter? nextFilter, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;
        if (nextFilter != null)
            nextFilter.Changed += NextFilter_Changed;
    }

    // roll a change announced below this stage up the pipe (this stage's own sets are unaffected)
    private void NextFilter_Changed(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // the setters raise Changed: the ranges are set/replaced at runtime (e.g. the server∩device allow
    // set arrives with the session), and the caches above must not serve verdicts of the old sets
    /// <summary>Gets or sets the address ranges whose flows are blocked. The default is empty.</summary>
    public IpRangeOrderedList BlockedRanges {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = [];

    /// <summary>Gets or sets the address ranges whose flows go around the tunnel. The default is empty.</summary>
    public IpRangeOrderedList ExcludeRanges {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = [];

    /// <summary>
    /// Gets or sets the address ranges that alone may take the default way; when not empty, a flow to an address
    /// outside them goes around the tunnel. The default is empty.
    /// </summary>
    public IpRangeOrderedList IncludeRanges {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = [];

    /// <summary>Gets or sets the destination ports whose TCP flows are blocked. The default is empty.</summary>
    /// <remarks>The setter copies the list.</remarks>
    public IReadOnlyList<PortRange> BlockedTcpPorts {
        get => _blockedTcpPorts;
        set { _blockedTcpPorts = value.ToArray(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Gets or sets the destination ports whose UDP flows are blocked. The default is empty.</summary>
    /// <remarks>The setter copies the list.</remarks>
    public IReadOnlyList<PortRange> BlockedUdpPorts {
        get => _blockedUdpPorts;
        set { _blockedUdpPorts = value.ToArray(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This stage's blocks come first, then the next stage; see the class's remarks for the order of the sets.
    /// </remarks>
    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        // The blocks before the next stage: asked first, its Include or Exclude took a destination past them, and
        // a server, which acts on Block alone, then reached what it blocks
        if (BlockedRanges.Count > 0 && BlockedRanges.Contains(endPoint.AddressValue))
            return FilterAction.Block;

        if (IsPortBlocked(protocol, endPoint.Port))
            return FilterAction.Block;

        var result = _nextFilter?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result != FilterAction.Default)
            return result;

        // excluded IP ranges
        if (ExcludeRanges.Count > 0 && ExcludeRanges.Contains(endPoint.AddressValue))
            return FilterAction.Exclude;

        // included IP ranges: a veto gate like every stage — a non-empty include set excludes non-members
        // and members pass as Default ("no objection"). Include is never returned by IP gates; it is an
        // explicit override lane (domain force-list, ICMP force).
        if (IncludeRanges.Count > 0 && !IncludeRanges.Contains(endPoint.AddressValue))
            return FilterAction.Exclude;

        return FilterAction.Default;
    }

    private bool IsPortBlocked(IpProtocol protocol, int port)
    {
        var blockedPorts = protocol switch {
            IpProtocol.Tcp => _blockedTcpPorts,
            IpProtocol.Udp => _blockedUdpPorts,
            _ => []
        };

        foreach (var portRange in blockedPorts)
            if (portRange.IsInRange(port))
                return true;

        return false;
    }

    /// <inheritdoc/>
    /// <remarks>It passes the call to the next stage: its own sets are no outside configuration.</remarks>
    public Task Reconfigure(CancellationToken cancellationToken) =>
        _nextFilter?.Reconfigure(cancellationToken) ?? Task.CompletedTask;

    /// <inheritdoc/>
    /// <value>
    /// <see langword="true"/> while the five sets are empty and the next stage, if any, is empty too.
    /// </value>
    public bool IsEmpty =>
        BlockedRanges.Count == 0 &&
        ExcludeRanges.Count == 0 &&
        IncludeRanges.Count == 0 &&
        _blockedTcpPorts.Length == 0 &&
        _blockedUdpPorts.Length == 0 &&
        (_nextFilter?.IsEmpty ?? true);

    /// <summary>Stops listening to the next stage, and disposes it when this stage was made to.</summary>
    public void Dispose()
    {
        // or a next stage that lives on would hold this one
        if (_nextFilter != null)
            _nextFilter.Changed -= NextFilter_Changed;

        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}