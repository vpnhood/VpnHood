namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyOptions
{
    public ProxyNode[] ProxyNodes { get; init; } = [];
    public bool ResetStates { get; set; }
}