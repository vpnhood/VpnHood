using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Utils;

public static class OsUtils
{
    public static string ExecuteCommand(string fileName, string command)
    {
        VhLogger.Instance.LogDebug($"Executing: {fileName} {command}");
        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = processInfo;
        process.Start();

        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();

        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new ExternalException(error, process.ExitCode);

        return output;
    }

    public static async Task<string> ExecuteCommandAsync(string fileName, string command, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug($"Executing: {fileName} {command}");
        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = processInfo;
        process.Start();

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);

        await WaitForExitAsync(process, cancellationToken);
        if (process.ExitCode != 0)
            throw new ExternalException(error, process.ExitCode);

        return output;
    }

    // fort .net standard compatibility
    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        // Attach event to complete task when process exits
        process.Exited += (_, _) => tcs.TrySetResult(true);
        process.EnableRaisingEvents = true;

        // Wait for process exit or cancellation
        await tcs.Task.WaitAsync(cancellationToken);
    }
}