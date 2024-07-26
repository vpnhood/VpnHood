using System.Diagnostics;
using System.Net;

namespace VpnHood.Server.App.Providers.Linux;


// add & remove ip address for linux server
public class LinuxNetConfigurationProvider : INetConfigurationProvider
{
    public async Task<string[]> GetInterfaceNames()
    {
        const string command = "ip link show | grep '^[0-9]' | awk '{print $2}' | sed 's/://'";
        var output = await ExecuteCommandAsync(command);
        var names = output
            .Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.Equals("lo", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return names;
    }

    public Task AddIpAddress(IPAddress ipAddress, string interfaceName)
    {
        var command = $"ip addr add {ipAddress}/32 dev {interfaceName}";
        return ExecuteCommandAsync(command);
    }

    public Task RemoveIpAddress(IPAddress ipAddress, string interfaceName)
    {
        var command = $"ip addr del {ipAddress}/32 dev {interfaceName}";
        return ExecuteCommandAsync(command);
    }

    public async Task<bool> IpAddressExists(IPAddress ipAddress)
    {
        const string command = "ip addr show";
        var output = await ExecuteCommandAsync(command);
        return output.Contains($" {ipAddress}/");
    }

    private static async Task<string> ExecuteCommandAsync(string command)
    {
        var processInfo = new ProcessStartInfo {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = processInfo;
        process.Start();

        var error = await process.StandardError.ReadToEndAsync();
        var output = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new Exception(error);

        return output;
    }

}