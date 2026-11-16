namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Specifies a filter's verdict on a flow.
/// </summary>
/// <remarks>
/// The client sends a flow through the tunnel unless a filter excludes or blocks it, so its IP filters only veto;
/// <see cref="Include"/> is the override a domain filter's include list uses, to send a flow through the tunnel past
/// every IP filter. The server acts on <see cref="Block"/> alone.
/// </remarks>
// Default means "no objection": in the client pipe undecided traffic tunnels, so IP gates only veto
// (Exclude/Block) and never return Include. Include is an explicit override lane — the domain force-list
// and the ICMP force use it to push traffic through the tunnel past every gate.
public enum FilterAction
{
    /// <summary>No objection: the flow takes its way by default, through the tunnel on the client.</summary>
    Default,

    /// <summary>Send the flow through the tunnel, whatever an IP filter says.</summary>
    Include,

    /// <summary>Send the flow around the tunnel, straight to its destination.</summary>
    Exclude,

    /// <summary>Drop the flow.</summary>
    Block
}
