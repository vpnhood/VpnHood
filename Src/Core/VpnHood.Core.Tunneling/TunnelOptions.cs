namespace VpnHood.Core.Tunneling;

public class TunnelOptions
{
    public int MaxDatagramChannelCount { get; init; } = 8;
    public int MaxQueueLength { get; init; } = 200;
}