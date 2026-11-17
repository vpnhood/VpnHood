using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using VpnHood.Core.Server.OsConfiguration;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

// ReSharper disable StringLiteralTypo

namespace VpnHood.App.Server.Providers.Linux;

/// <summary>Reads and changes a Linux machine's OS for the server, through its commands and <c>/proc/sys</c>.</summary>
public class LinuxOsConfigurationProvider : IOsConfigurationProvider
{
    private const string TcpCongestionControlPath = "/proc/sys/net/ipv4/tcp_congestion_control";
    private const string TcpAvailableCongestionControlPath = "/proc/sys/net/ipv4/tcp_available_congestion_control";
    private const string DefaultQdiscPath = "/proc/sys/net/core/default_qdisc";
    private const string ConnectionTrackingMaxPath = "/proc/sys/net/netfilter/nf_conntrack_max";
    private const string ConnectionTrackingTcpIdleTimeoutPath =
        "/proc/sys/net/netfilter/nf_conntrack_tcp_timeout_established";
    private const string SwapFilePath = "/vpnhood.swap";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetInterfaceNames(CancellationToken cancellationToken)
    {
        const string command = "ip link show | grep '^[0-9]' | awk '{print $2}' | sed 's/://'";
        var output = await ExecuteCommandAsync(command, cancellationToken).Vhc();
        return output
            .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.Equals("lo", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <inheritdoc/>
    public async Task<bool> IpAddressExists(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var output = await ExecuteCommandAsync("ip addr show", cancellationToken).Vhc();
        return output.Contains($" {ipAddress}/");
    }

    /// <inheritdoc/>
    public Task AddIpAddress(IPAddress ipAddress, string interfaceName, CancellationToken cancellationToken)
    {
        ValidateInterfaceName(interfaceName);
        var subnet = ipAddress.IsV4 ? 32 : 128;
        return ExecuteCommandAsync($"ip addr add {ipAddress}/{subnet} dev {interfaceName}", cancellationToken);
    }

    /// <inheritdoc/>
    public void RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        ValidateInterfaceName(interfaceName);
        var subnet = ipAddress.IsV4 ? 32 : 128;
        OsUtils.ExecuteShellCommand($"ip addr del {ipAddress}/{subnet} dev {interfaceName}");
    }

    /// <inheritdoc/>
    public Task<string> GetTcpCongestionControl(CancellationToken cancellationToken)
    {
        return ReadProcSys(TcpCongestionControlPath, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SetTcpCongestionControl(string name, CancellationToken cancellationToken)
    {
        // the name goes into a command run as root, so a kernel algorithm's name only
        if (!Regex.IsMatch(name, @"\A[a-z0-9_]{1,16}\z"))
            throw new ArgumentException($"Invalid TCP congestion control name. Name: {name}", nameof(name));

        ThrowIfProcSysReadOnly();

        // a module only for one the kernel lacks: reno is built in and has none
        if (!await IsTcpCongestionControlAvailable(name, cancellationToken).Vhc()) {
            await ExecuteCommandAsync($"modprobe tcp_{name} 2>/dev/null || true", cancellationToken).Vhc();
            if (!await IsTcpCongestionControlAvailable(name, cancellationToken).Vhc())
                throw new NotSupportedException($"The kernel has no {name} congestion control.");
        }

        await WriteProcSys(DefaultQdiscPath, "fq", cancellationToken).Vhc();
        await WriteProcSys(TcpCongestionControlPath, name, cancellationToken).Vhc();
    }

    /// <inheritdoc/>
    public async Task<int> GetConnectionTrackingMax(CancellationToken cancellationToken)
    {
        ThrowIfNoConnectionTracking();
        var text = await ReadProcSys(ConnectionTrackingMaxPath, cancellationToken).Vhc();
        return int.Parse(text, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public Task SetConnectionTrackingMax(int max, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);
        ThrowIfProcSysReadOnly();
        ThrowIfNoConnectionTracking();
        return WriteProcSys(ConnectionTrackingMaxPath, max.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TimeSpan> GetConnectionTrackingTcpIdleTimeout(CancellationToken cancellationToken)
    {
        ThrowIfNoConnectionTracking();
        var text = await ReadProcSys(ConnectionTrackingTcpIdleTimeoutPath, cancellationToken).Vhc();
        return TimeSpan.FromSeconds(long.Parse(text, CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public Task SetConnectionTrackingTcpIdleTimeout(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.FromSeconds(1));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, TimeSpan.FromSeconds(int.MaxValue));
        ThrowIfProcSysReadOnly();
        ThrowIfNoConnectionTracking();
        return WriteProcSys(ConnectionTrackingTcpIdleTimeoutPath,
            ((long)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SwapMemoryInfo> GetSwapMemoryInfo(CancellationToken cancellationToken)
    {
        var swapFiles = await ListCurrentSwapFiles(cancellationToken).Vhc();
        return new SwapMemoryInfo {
            TotalSize = swapFiles.Sum(x => x.Size),
            TotalUsed = swapFiles.Sum(x => x.Used),
            AppSize = swapFiles.Where(x => x.FilePath == SwapFilePath).Sum(x => x.Size),
            AppUsed = swapFiles.Where(x => x.FilePath == SwapFilePath).Sum(x => x.Used)
        };
    }

    /// <inheritdoc/>
    /// <remarks>A size under 1,000,000 bytes is none.</remarks>
    public async Task SetAppSwapMemorySize(long size, CancellationToken cancellationToken)
    {
        // the current file goes first, out of use
        if (File.Exists(SwapFilePath)) {
            var swapFiles = await ListCurrentSwapFiles(cancellationToken).Vhc();
            if (swapFiles.Any(x => x.FilePath == SwapFilePath))
                await ExecuteCommandAsync($"swapoff {SwapFilePath}", cancellationToken).Vhc();

            File.Delete(SwapFilePath);
        }

        if (size < 1_000_000)
            return;

        // not in /etc/fstab: the server turns it on again at each start
        await ExecuteCommandAsync($"fallocate -l {size} {SwapFilePath}", cancellationToken).Vhc();
        await ExecuteCommandAsync($"chmod 600 {SwapFilePath}", cancellationToken).Vhc();
        await ExecuteCommandAsync($"mkswap {SwapFilePath}", cancellationToken).Vhc();
        await ExecuteCommandAsync($"swapon {SwapFilePath}", cancellationToken).Vhc();
    }

    private static async Task<bool> IsTcpCongestionControlAvailable(string name, CancellationToken cancellationToken)
    {
        var available = await ReadProcSys(TcpAvailableCongestionControlPath, cancellationToken).Vhc();
        return available.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Contains(name);
    }

    // a container's kernel settings are its host's: its /proc/sys is read-only
    private static void ThrowIfProcSysReadOnly()
    {
        if (IsProcSysReadOnly())
            throw new NotSupportedException("The kernel's settings are read-only here, as in a container.");
    }

    // the kernel tracks connections once a NAT rule needs it
    private static void ThrowIfNoConnectionTracking()
    {
        if (!File.Exists(ConnectionTrackingMaxPath))
            throw new NotSupportedException("The OS tracks no connections here.");
    }

    // by the mount table, whose last /proc/sys mount is the one in effect; a mountinfo line holds the id, the
    // parent's, major:minor, the root, the mount point and its options, then more
    private static bool IsProcSysReadOnly()
    {
        return File.ReadLines("/proc/self/mountinfo")
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length > 5 && fields[4] == "/proc/sys")
            .Select(fields => fields[5].Split(',').Contains("ro"))
            .LastOrDefault();
    }

    private static async Task<string> ReadProcSys(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).Vhc();
        return text.Trim();
    }

    // a network namespace of its own, as a container's, takes some settings only from the machine's own
    private static async Task WriteProcSys(string path, string value, CancellationToken cancellationToken)
    {
        try {
            await File.WriteAllTextAsync(path, value, cancellationToken).Vhc();
        }
        catch (UnauthorizedAccessException ex) {
            throw new NotSupportedException($"The kernel takes no change of {Path.GetFileName(path)} from here.", ex);
        }
    }

    private static void ValidateInterfaceName(string interfaceName)
    {
        // the name goes into a command run as root
        if (!Regex.IsMatch(interfaceName, @"\A[a-zA-Z0-9_-]+\z"))
            throw new ArgumentException($"Invalid network interface name. InterfaceName: {interfaceName}",
                nameof(interfaceName));
    }

    private static async Task<IReadOnlyList<LinuxSwapFile>> ListCurrentSwapFiles(CancellationToken cancellationToken)
    {
        var output = await ExecuteCommandAsync("swapon --show --bytes", cancellationToken).Vhc();
        return output
            .Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("NAME", StringComparison.Ordinal))
            .Select(ParseSwapFileLine)
            .OfType<LinuxSwapFile>()
            .ToArray();
    }

    // a line holds NAME TYPE SIZE USED PRIO
    private static LinuxSwapFile? ParseSwapFileLine(string line)
    {
        var columns = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 4 ||
            !long.TryParse(columns[2], out var size) ||
            !long.TryParse(columns[3], out var used))
            return null;

        return new LinuxSwapFile(columns[0], size, used);
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteShellCommandAsync(command, cancellationToken);
    }
}
