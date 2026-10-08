using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

public static class OsUtils
{
    public static string ExecuteCommand(string fileName, string command)
    {
        return ExecuteCommand(fileName, command, Timeout.InfiniteTimeSpan);
    }

    // a command still running at the timeout is killed
    public static string ExecuteCommand(string fileName, string command, TimeSpan timeout)
    {
        VhLogger.Instance.LogDebug($"Executing: {fileName} {command}");
        var processInfo = new ProcessStartInfo {
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

        // both streams at once: reading one to its end first stalls a command that fills the other's pipe
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(timeout)) {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"The command has not finished in {timeout.TotalSeconds} seconds. Command: {fileName} {command}.");
        }

        var error = errorTask.GetAwaiter().GetResult();
        var output = outputTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new ExternalException(error, process.ExitCode);

        return output;
    }

    public static async Task<string> ExecuteCommandAsync(string fileName, string command,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug($"Executing: {fileName} {command}");
        var processInfo = new ProcessStartInfo {
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

        // both streams at once, as in ExecuteCommand
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await errorTask;
        var output = await outputTask;

        await WaitForExitAsync(process, cancellationToken);
        if (process.ExitCode != 0) {
            error += $". Command: {fileName} {command}.";
            throw new ExternalException(error, process.ExitCode);
        }

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