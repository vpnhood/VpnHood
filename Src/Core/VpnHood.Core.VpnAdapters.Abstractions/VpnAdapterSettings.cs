namespace VpnHood.Core.VpnAdapters.Abstractions;

public class VpnAdapterSettings
{
    public required string AdapterName { get; init; }
    public int MaxPacketCount { get; init; } = 255;
    public TimeSpan MaxPacketSendDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public int MaxAutoRestartCount { get; init; }
    
    /// <summary>
    /// Automatically adjusts route metrics by splitting routes when all routes are included.
    /// This helps to avoid conflicts and ensures proper prioritization of network routes.
    /// </summary>
    public bool AutoMetric { get; init; } = true;
}