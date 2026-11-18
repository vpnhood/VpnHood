using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.Server.OsConfiguration;

namespace VpnHood.Test.Providers;

public class TestOsConfigurationProvider : IOsConfigurationProvider
{
    private int _changeCount;

    public ConcurrentDictionary<IPAddress, string> IpAddresses { get; } = new();
    public TestHold AddHold { get; } = new();

    // null where the machine lacks it, as Linux's own values
    public string? TcpCongestionControl { get; set; } = "cubic";
    public IReadOnlyList<string> AvailableTcpCongestionControls { get; set; } = ["reno", "cubic", "bbr"];
    public int? ConnectionTrackingMax { get; set; } = 262_144;
    public TimeSpan ConnectionTrackingTcpIdleTimeout { get; set; } = TimeSpan.FromDays(5);

    public long AppSize { get; set; }
    public long AppUsed { get; set; }
    public long OtherSize { get; set; }
    public long OtherUsed { get; set; }
    public int ChangeCount => _changeCount;

    public SwapMemoryInfo Info => new() {
        AppSize = AppSize,
        AppUsed = AppUsed,
        TotalSize = OtherSize + AppSize,
        TotalUsed = OtherUsed + AppUsed
    };

    public Task<IReadOnlyList<string>> GetInterfaceNames(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(["eth0", "eth1"]);
    }

    public Task<bool> IpAddressExists(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        return Task.FromResult(IpAddresses.ContainsKey(ipAddress));
    }

    public async Task AddIpAddress(IPAddress ipAddress, string interfaceName, CancellationToken cancellationToken)
    {
        await AddHold.WaitIfHeld(cancellationToken);
        IpAddresses.TryAdd(ipAddress, interfaceName);
        Interlocked.Increment(ref _changeCount);
    }

    public void RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        IpAddresses.TryRemove(ipAddress, out _);
    }

    public Task<string> GetTcpCongestionControl(CancellationToken cancellationToken)
    {
        return Task.FromResult(TcpCongestionControl ?? throw new NotSupportedException("No congestion control here."));
    }

    public Task SetTcpCongestionControl(string name, CancellationToken cancellationToken)
    {
        if (TcpCongestionControl == null)
            throw new NotSupportedException("No congestion control here.");

        if (!AvailableTcpCongestionControls.Contains(name))
            throw new NotSupportedException($"The kernel has no {name} congestion control.");

        TcpCongestionControl = name;
        Interlocked.Increment(ref _changeCount);
        return Task.CompletedTask;
    }

    public Task<int> GetConnectionTrackingMax(CancellationToken cancellationToken)
    {
        return Task.FromResult(ConnectionTrackingMax ?? throw new NotSupportedException("No connection tracking here."));
    }

    public Task SetConnectionTrackingMax(int max, CancellationToken cancellationToken)
    {
        if (ConnectionTrackingMax == null)
            throw new NotSupportedException("No connection tracking here.");

        ConnectionTrackingMax = max;
        Interlocked.Increment(ref _changeCount);
        return Task.CompletedTask;
    }

    public Task<TimeSpan> GetConnectionTrackingTcpIdleTimeout(CancellationToken cancellationToken)
    {
        if (ConnectionTrackingMax == null)
            throw new NotSupportedException("No connection tracking here.");

        return Task.FromResult(ConnectionTrackingTcpIdleTimeout);
    }

    public Task SetConnectionTrackingTcpIdleTimeout(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (ConnectionTrackingMax == null)
            throw new NotSupportedException("No connection tracking here.");

        ConnectionTrackingTcpIdleTimeout = timeout;
        Interlocked.Increment(ref _changeCount);
        return Task.CompletedTask;
    }

    public Task<SwapMemoryInfo> GetSwapMemoryInfo(CancellationToken cancellationToken)
    {
        return Task.FromResult(Info);
    }

    public Task SetAppSwapMemorySize(long size, CancellationToken cancellationToken)
    {
        AppSize = size;
        Interlocked.Increment(ref _changeCount);
        return Task.CompletedTask;
    }
}
