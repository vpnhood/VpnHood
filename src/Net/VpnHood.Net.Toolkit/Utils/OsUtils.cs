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
        return Execute(CreateStartInfo(fileName, command), $"{fileName} {command}", timeout);
    }

    public static Task<string> ExecuteCommandAsync(string fileName, string command,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(CreateStartInfo(fileName, command), $"{fileName} {command}", cancellationToken);
    }

    // A command line for bash, given to it as one argument, as written. Never through Arguments, which parses
    // its quotes again by Windows' rules: an inner double quote could change the command bash runs
    public static string ExecuteShellCommand(string command)
    {
        return Execute(CreateShellStartInfo(command), $"/bin/bash -c {command}", Timeout.InfiniteTimeSpan);
    }

    public static Task<string> ExecuteShellCommandAsync(string command, CancellationToken cancellationToken)
    {
        return ExecuteAsync(CreateShellStartInfo(command), $"/bin/bash -c {command}", cancellationToken);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string arguments)
    {
        return new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static ProcessStartInfo CreateShellStartInfo(string command)
    {
        var startInfo = CreateStartInfo("/bin/bash", arguments: "");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static string Execute(ProcessStartInfo startInfo, string commandLine, TimeSpan timeout)
    {
        VhLogger.Instance.LogDebug($"Executing: {commandLine}");
        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        // both streams at once: reading one to its end first stalls a command that fills the other's pipe
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(timeout)) {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"The command has not finished in {timeout.TotalSeconds} seconds. Command: {commandLine}.");
        }

        var error = errorTask.GetAwaiter().GetResult();
        var output = outputTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new ExternalException($"{error}. Command: {commandLine}.", process.ExitCode);

        return output;
    }

    private static async Task<string> ExecuteAsync(ProcessStartInfo startInfo, string commandLine,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug($"Executing: {commandLine}");
        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        // both streams at once, as in Execute
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await errorTask;
        var output = await outputTask;

        await WaitForExitAsync(process, cancellationToken);
        if (process.ExitCode != 0) {
            error += $". Command: {commandLine}.";
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