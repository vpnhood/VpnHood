using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public class VpnAdapterSettings
{
    public required string AdapterName { get; init; }
    public ILogger Logger { get; init; } = VhLogger.Instance;
    public int MaxPacketCount { get; init; } = 255;
    public TimeSpan MaxPacketSendDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public int MaxAutoRestartCount { get; init; }
}