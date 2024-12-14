using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Net;
using VpnHood.Server.Abstractions;

namespace VpnHood.Apps.Server.Providers.Linux;


// add & remove ip address for linux server
public class LinuxNetConfigurationProvider(ILogger logger) : INetConfigurationProvider
{
    public async Task<string> GetTcpCongestionControl()
    {
        var active = await LinuxUtils.ExecuteCommandAsync("sysctl net.ipv4.tcp_congestion_control");
        return active.Split('=').Last().Trim();
    }

    public async Task SetTcpCongestionControl(string value)
    {
        // Check if the value is already set
        if (await GetTcpCongestionControl() == value)
            return;

        logger.LogInformation("Updating TcpCongestionControl to {value}...", value);
        await LinuxUtils.ExecuteCommandAsync($"modprobe tcp_{value}");

        var available = await LinuxUtils.ExecuteCommandAsync("sysctl net.ipv4.tcp_available_congestion_control");
        if (!available.Contains(value, StringComparison.CurrentCultureIgnoreCase))
            throw new InvalidOperationException($"{value} is not available.");

        // ReSharper disable StringLiteralTypo
        // Set new config
        await LinuxUtils.ExecuteCommandAsync("sysctl -w net.core.default_qdisc=fq");
        await LinuxUtils.ExecuteCommandAsync($"sysctl -w net.ipv4.tcp_congestion_control={value}");
        // ReSharper restore StringLiteralTypo
    }

    private static void ValidateInterfaceName(string interfaceName)
    {
        const string pattern = "^[a-zA-Z0-9_-]+$";
        var regex = new Regex(pattern);

        // Validate user input
        if (!regex.IsMatch(interfaceName))
            throw new InvalidOperationException($"Invalid network interface name. InterfaceName: {interfaceName}");
    }

    public async Task<string[]> GetInterfaceNames()
    {
        const string command = "ip link show | grep '^[0-9]' | awk '{print $2}' | sed 's/://'";
        var output = await LinuxUtils.ExecuteCommandAsync(command);
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
        return LinuxUtils.ExecuteCommandAsync(command);
    }

    public Task RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        ValidateInterfaceName(interfaceName);

        var subnet = ipAddress.IsV4() ? 32 : 128;
        var command = $"ip addr del {ipAddress}/{subnet} dev {interfaceName}";
        return LinuxUtils.ExecuteCommandAsync(command);
    }

    public async Task<bool> IpAddressExists(IPAddress ipAddress)
    {
        const string command = "ip addr show";
        var output = await LinuxUtils.ExecuteCommandAsync(command);
        return output.Contains($" {ipAddress}/");
    }
}