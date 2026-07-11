namespace VpnHood.Core.Filtering.Abstractions;

// Default means "no objection": in the client pipe undecided traffic tunnels, so IP gates only veto
// (Exclude/Block) and never return Include. Include is an explicit override lane — the domain force-list
// and the ICMP force use it to push traffic through the tunnel past every gate.
public enum FilterAction
{
    Default,
    Include,
    Exclude,
    Block
}
