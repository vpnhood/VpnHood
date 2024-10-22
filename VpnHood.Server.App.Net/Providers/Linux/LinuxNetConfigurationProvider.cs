using System.Net;
using System.Text.RegularExpressions;
using VpnHood.Common.Net;
using VpnHood.Server.Abstractions;

namespace VpnHood.Server.App.Providers.Linux;


// add & remove ip address for linux server
public class LinuxNetConfigurationProvider : INetConfigurationProvider
{
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