using System.Diagnostics;

namespace VpnHood.App.Server.Providers.Linux;

internal class LinuxUtils
{
    public static async Task<string> ExecuteCommandAsync(string command)
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