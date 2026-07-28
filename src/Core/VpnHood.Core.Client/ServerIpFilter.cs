using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Client;

/// <summary>
/// The filter stage that speaks for the SERVER: its include ranges hold the server's routing declaration
/// (app ∩ adapter ranges) and nothing else — deliberately no exclude or block lists, so no rule of this
/// stage can ever route traffic around the tunnel behind UnsupportedIpMode's back. A destination the server
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

    public UnsupportedIpMode UnsupportedIpMode {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = UnsupportedIpMode.Exclude;

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        // the stages below speak for the client (the user's splits and blocks); their veto is final
        var result = _nextFilter?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result is FilterAction.Block or FilterAction.Exclude)
            return result;

        // the server's word: a member passes unchanged (Default, or a preserved inner Include from an
        // override lane) and a non-member takes the mode's action — except a refused Include, which is
        // blocked under either mode: it promised to travel inside, so letting it out would leak it
        if (!IncludeRanges.Contains(endPoint.Address)) {

            // block if server refused an Include
            if (UnsupportedIpMode is UnsupportedIpMode.Block)
                return FilterAction.Block;

            // default to Exclude if server refused an Include
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
        (_nextFilter?.IsEmpty ?? true);

    public void Dispose()
    {
        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}
