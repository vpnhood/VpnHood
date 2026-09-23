using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Client;

/// <summary>
/// The filter stage that speaks for the SERVER: its include ranges hold the server's routing declaration
/// (app ∩ adapter ranges) and nothing else — deliberately no exclude or block lists, so no rule of this
/// stage can ever route traffic around the tunnel behind SplitUnsupportedIpMode's back. A destination the server
/// does not route becomes the mode's action: Exclude (bypass) or Block (fail-closed).
/// The client's word keeps its power in one direction only: an inner Exclude or Block is final (the user's
/// own splits bypass even under Block), while an inner Include is preserved only for destinations the
/// server routes. A refused Include is BLOCKED regardless of the mode: Include is a promise that the
/// traffic travels inside the tunnel, and excluding it would leak the very traffic the promise covers.
/// </summary>
internal class ServerIpFilter : IIpFilter
{
    private readonly IIpFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;

    public event EventHandler? Changed;

    public ServerIpFilter(IIpFilter? nextFilter, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        // roll a change announced below this stage up the pipe (this stage's own rules are unaffected)
        if (nextFilter != null)
            nextFilter.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    // The setters raise Changed: the server's declaration arrives with the session, and the caches above
    // must not serve verdicts of the old rules. An empty declaration means "no restriction" and is
    // converted to All at the door, so Process never needs an empty special-case and "before the session"
    // and "server routes everything" are one honest state.
    public IpRangeOrderedList IncludeRanges {
        get;
        set {
            field = value.Count > 0 ? value : IpNetwork.All.ToIpRanges();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = IpNetwork.All.ToIpRanges();

    public SplitUnsupportedIpMode UnroutedIpMode {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = SplitUnsupportedIpMode.Exclude;

    // The fate of IPv6 when the server cannot carry the family at all; a v6 miss inside a SUPPORTED
    // family's narrow ranges takes the regular UnroutedIpMode like any other destination.
    // UnroutedIpMode is superior and Process enforces it: a general Block kills the family even if
    // this mode says Exclude (the app additionally passes the pair resolved).
    public SplitUnsupportedIpMode UnsupportedIpV6Mode {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = SplitUnsupportedIpMode.Block;

    // When false, every IPv6 destination is a miss regardless of IncludeRanges (a server that cannot
    // carry the family may still have declared no restriction), judged by UnsupportedIpV6Mode.
    public bool IsIpV6SupportedByServer {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = true;

    /// <summary>
    /// The server's word alone — no client gate consulted. An address is routable when the server can
    /// carry its family and declares it in its ranges. The family is checked first because it overrides
    /// the declaration: a server that cannot carry IPv6 may still have declared no restriction, and an
    /// unrestricted declaration covers both families.
    /// </summary>
    public bool CanServerRoute(IPAddress ipAddress)
    {
        if (ipAddress.IsV6() && !IsIpV6SupportedByServer)
            return false;

        return IncludeRanges.Contains(ipAddress);
    }

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        // the stages below speak for the client (the user's splits and blocks); their veto is final
        var result = _nextFilter?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result is FilterAction.Block or FilterAction.Exclude)
            return result;

        // the server's word: a member passes unchanged (Default, or a preserved inner Include from an
        // override lane) and a non-member takes the mode's action — except a refused Include, which is
        // blocked under either mode: it promised to travel inside, so letting it out would leak it
        var isFamilyUnsupported = !IsIpV6SupportedByServer && endPoint.IsV6();
        if (isFamilyUnsupported || !IncludeRanges.Contains(endPoint.Address)) {
            // the same two steps as BuildIncludeIpRangesByDevice: an unrouted destination takes the
            // general mode and an unsupported family takes its own...
            var missMode = isFamilyUnsupported ? UnsupportedIpV6Mode : UnroutedIpMode;

            // ...and UnroutedIpMode is superior: its Block kills the family even when the v6 mode
            // says Exclude, so the rule holds no matter how the values arrived
            if (UnroutedIpMode is SplitUnsupportedIpMode.Block)
                missMode = SplitUnsupportedIpMode.Block;

            if (missMode is SplitUnsupportedIpMode.Block)
                return FilterAction.Block;

            // a refused Include is blocked even under Exclude: it promised to travel inside
            return result is FilterAction.Include
                ? FilterAction.Block
                : FilterAction.Exclude;
        }

        return result;
    }

    // this stage's own rules are program state, not external configuration; just forward the command
    public void Reconfigure() => _nextFilter?.Reconfigure();

    public bool IsEmpty =>
        IncludeRanges.IsAll() &&
        IsIpV6SupportedByServer &&
        (_nextFilter?.IsEmpty ?? true);

    public void Dispose()
    {
        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}
