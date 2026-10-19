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
    public async Task<string> GetTcpCongestionControl()
    {
        var active = await ExecuteCommandAsync("sysctl net.ipv4.tcp_congestion_control").Vhc();
        return active.Split('=').Last().Trim();
    }

    public async Task SetTcpCongestionControl(string value)
    {
        // the access manager's value goes into root's commands, so a kernel algorithm's name only
        if (!Regex.IsMatch(value, @"\A[a-z0-9_]{1,16}\z"))
            throw new ArgumentException($"Invalid TCP congestion control name. Value: {value}", nameof(value));

        // Check if the value is already set
        if (await GetTcpCongestionControl().Vhc() == value)
            return;

        logger.LogInformation("Updating TcpCongestionControl to {value}...", value);

        // a module only for one the kernel lacks: reno is built in and has none
        if (!await IsTcpCongestionControlAvailable(value).Vhc()) {
            await ExecuteCommandAsync($"modprobe tcp_{value}").Vhc();
            if (!await IsTcpCongestionControlAvailable(value).Vhc())
                throw new InvalidOperationException($"{value} is not available.");
        }

        // ReSharper disable StringLiteralTypo
        // Set new config
        await ExecuteCommandAsync("sysctl -w net.core.default_qdisc=fq").Vhc();
        await ExecuteCommandAsync($"sysctl -w net.ipv4.tcp_congestion_control={value}").Vhc();
        // ReSharper restore StringLiteralTypo
    }

    private static async Task<bool> IsTcpCongestionControlAvailable(string value)
    {
        var available = await ExecuteCommandAsync("sysctl net.ipv4.tcp_available_congestion_control").Vhc();
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

    public async Task<string[]> GetInterfaceNames()
    {
        const string command = "ip link show | grep '^[0-9]' | awk '{print $2}' | sed 's/://'";
        var output = await ExecuteCommandAsync(command).Vhc();
        var names = output
            .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.Equals("lo", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return names;
    }

    public Task AddIpAddress(IPAddress ipAddress, string interfaceName)
    {
        ValidateInterfaceName(interfaceName);

        var subnet = ipAddress.IsV4() ? 32 : 128;
        var command = $"ip addr add {ipAddress}/{subnet} dev {interfaceName}";
        return ExecuteCommandAsync(command);
    }

    public void RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        ValidateInterfaceName(interfaceName);

        var subnet = ipAddress.IsV4() ? 32 : 128;
        var command = $"ip addr del {ipAddress}/{subnet} dev {interfaceName}";
        OsUtils.ExecuteShellCommand(command);
    }

    public async Task<bool> IpAddressExists(IPAddress ipAddress)
    {
        const string command = "ip addr show";
        var output = await ExecuteCommandAsync(command).Vhc();
        return output.Contains($" {ipAddress}/");
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        return OsUtils.ExecuteShellCommandAsync(command, cancellationToken);
    }
}