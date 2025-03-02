using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VpnHood.Core.VpnAdapters.LinuxTun;

public class LinuxTunVpnAdapterOptions
{
    public required string AdapterName { get; init; }
    public ILogger Logger { get; init; } = NullLogger<LinuxTunVpnAdapter>.Instance;
    public int MaxPacketCount { get; init; }
    public TimeSpan MaxPacketSendDelay { get; init; } = TimeSpan.FromMilliseconds(500);

}