using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.App.Server.Providers.Linux;

// add & remove ip address for linux server
public class LinuxNetConfigurationProvider(ILogger logger) : INetConfigurationProvider
{
    private const string ConnectionTrackingMaxPath = "/proc/sys/net/netfilter/nf_conntrack_max";
    private const string ConnectionTrackingBucketsPath = "/proc/sys/net/netfilter/nf_conntrack_buckets";
    private const string ConnectionTrackingTcpIdleTimeoutPath =
        "/proc/sys/net/netfilter/nf_conntrack_tcp_timeout_established";

    public async Task<string> GetTcpCongestionControl(CancellationToken cancellationToken)
    {
        var active = await ExecuteCommandAsync("sysctl net.ipv4.tcp_congestion_control", cancellationToken).Vhc();
        return active.Split('=').Last().Trim();
    }

    public async Task SetTcpCongestionControl(string value, CancellationToken cancellationToken)
    {
        // the access manager's value goes into root's commands, so a kernel algorithm's name only
        if (!Regex.IsMatch(value, @"\A[a-z0-9_]{1,16}\z"))
            throw new ArgumentException($"Invalid TCP congestion control name. Value: {value}", nameof(value));

        // Check if the value is already set
        if (await GetTcpCongestionControl(cancellationToken).Vhc() == value)
            return;

        // a container's kernel settings are its host's: its /proc/sys is read-only, and it has no modprobe
        if (IsProcSysReadOnly()) {
            logger.LogInformation(
                "TcpCongestionControl is left to the host, as /proc/sys is read-only here (a container). Wanted: {value}",
                value);
            return;
        }

        logger.LogInformation("Updating TcpCongestionControl to {value}...", value);

        // a module only for one the kernel lacks: reno is built in and has none
        if (!await IsTcpCongestionControlAvailable(value, cancellationToken).Vhc()) {
            await ExecuteCommandAsync($"modprobe tcp_{value}", cancellationToken).Vhc();
            if (!await IsTcpCongestionControlAvailable(value, cancellationToken).Vhc())
                throw new InvalidOperationException($"{value} is not available.");
        }

        // ReSharper disable StringLiteralTypo
        // Set new config
        await ExecuteCommandAsync("sysctl -w net.core.default_qdisc=fq", cancellationToken).Vhc();
        await ExecuteCommandAsync($"sysctl -w net.ipv4.tcp_congestion_control={value}", cancellationToken).Vhc();
        // ReSharper restore StringLiteralTypo
    }

    public async Task SetConnectionTracking(int max, TimeSpan tcpIdleTimeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(max);
        ArgumentOutOfRangeException.ThrowIfLessThan(tcpIdleTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tcpIdleTimeout, TimeSpan.FromSeconds(int.MaxValue));

        if (IsProcSysReadOnly()) {
            logger.LogInformation(
                "Connection tracking is left to the host, as /proc/sys is read-only here (a container).");
            return;
        }

        // the kernel tracks connections once a NAT rule needs it, and the VPN adapter adds its rules before
        if (!File.Exists(ConnectionTrackingMaxPath)) {
            logger.LogInformation("Connection tracking is left as it is, as the OS tracks no connections here.");
            return;
        }

        // the timeout first: a network namespace of its own, as a container's, takes it but not the max
        if (tcpIdleTimeout > TimeSpan.Zero)
            await SetConnectionTrackingTcpIdleTimeout(tcpIdleTimeout, cancellationToken).Vhc();

        if (max > 0)
            await RaiseConnectionTrackingMax(max, cancellationToken).Vhc();
    }

    private async Task SetConnectionTrackingTcpIdleTimeout(TimeSpan tcpIdleTimeout,
        CancellationToken cancellationToken)
    {
        var seconds = Math.Max(1, (long)tcpIdleTimeout.TotalSeconds);
        var current = await ReadProcSysNumber(ConnectionTrackingTcpIdleTimeoutPath, cancellationToken).Vhc();
        if (current == seconds)
            return;

        logger.LogInformation("Updating connection tracking's TCP idle timeout from {Current}s to {Seconds}s...",
            current, seconds);
        await WriteProcSysNumber(ConnectionTrackingTcpIdleTimeoutPath, seconds, cancellationToken).Vhc();
    }

    // never lowered: a machine with more memory has a larger table of its own
    private async Task RaiseConnectionTrackingMax(int max, CancellationToken cancellationToken)
    {
        var current = await ReadProcSysNumber(ConnectionTrackingMaxPath, cancellationToken).Vhc();
        if (max <= current)
            return;

        // at least a quarter as many buckets, so a full table's lookups stay short; the kernel takes them only from
        // its own network namespace, and the larger table works without them
        var buckets = await ReadProcSysNumber(ConnectionTrackingBucketsPath, cancellationToken).Vhc();
        if (buckets < max / 4) {
            try {
                await WriteProcSysNumber(ConnectionTrackingBucketsPath, max / 4, cancellationToken).Vhc();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                logger.LogWarning(ex, "Could not set connection tracking's buckets. Buckets: {Buckets}", max / 4);
            }
        }

        logger.LogInformation("Updating connection tracking's max from {Current} to {Max}...", current, max);
        await WriteProcSysNumber(ConnectionTrackingMaxPath, max, cancellationToken).Vhc();
    }

    private static async Task<long> ReadProcSysNumber(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).Vhc();
        return long.Parse(text.Trim(), CultureInfo.InvariantCulture);
    }

    private static Task WriteProcSysNumber(string path, long value, CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(path, value.ToString(CultureInfo.InvariantCulture), cancellationToken);
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

    private static async Task<bool> IsTcpCongestionControlAvailable(string value, CancellationToken cancellationToken)
    {
        var available = await ExecuteCommandAsync("sysctl net.ipv4.tcp_available_congestion_control", cancellationToken)
            .Vhc();
        return available.Split('=').Last().Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Contains(value);
    }

    private static void ValidateInterfaceName(string interfaceName)
    {
        const string pattern = @"\A[a-zA-Z0-9_-]+\z";
        var regex = new Regex(pattern);

        // Validate user input
        if (!regex.IsMatch(interfaceName))
            throw new InvalidOperationException($"Invalid network interface name. InterfaceName: {interfaceName}");
    }

    public async Task<string[]> GetInterfaceNames(CancellationToken cancellationToken)
    {
        const string command = "ip link show | grep '^[0-9]' | awk '{print $2}' | sed 's/://'";
        var output = await ExecuteCommandAsync(command, cancellationToken).Vhc();
        var names = output
            .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.Equals("lo", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return names;
    }

    public Task AddIpAddress(IPAddress ipAddress, string interfaceName, CancellationToken cancellationToken)
    {
        ValidateInterfaceName(interfaceName);

        var subnet = ipAddress.IsV4 ? 32 : 128;
        var command = $"ip addr add {ipAddress}/{subnet} dev {interfaceName}";
        return ExecuteCommandAsync(command, cancellationToken);
    }

    public void RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        ValidateInterfaceName(interfaceName);

        var subnet = ipAddress.IsV4 ? 32 : 128;
        var command = $"ip addr del {ipAddress}/{subnet} dev {interfaceName}";
        OsUtils.ExecuteShellCommand(command);
    }

    public async Task<bool> IpAddressExists(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        const string command = "ip addr show";
        var output = await ExecuteCommandAsync(command, cancellationToken).Vhc();
        return output.Contains($" {ipAddress}/");
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteShellCommandAsync(command, cancellationToken);
    }
}