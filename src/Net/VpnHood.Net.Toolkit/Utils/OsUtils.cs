using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides the means to run a program or a shell command and read its output.
/// </summary>
public static class OsUtils
{
    /// <summary>Runs a program with its arguments, and waits for it.</summary>
    /// <param name="fileName">The program.</param>
    /// <param name="command">The arguments, as one string.</param>
    /// <returns>The program's standard output.</returns>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The command ended with an exit code other than 0, which is the exception's error code; its error output, with the
    /// command line, is the message.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The program cannot be started.</exception>
    public static string ExecuteCommand(string fileName, string command)
    {
        return ExecuteCommand(fileName, command, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Runs a program with its arguments, and waits for it at most a timeout, after which it is killed; reading the rest of
    /// its output then is not timed.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="command">The arguments, as one string.</param>
    /// <param name="timeout">How long to wait, or <see cref="Timeout.InfiniteTimeSpan"/> for no limit.</param>
    /// <returns>The program's standard output.</returns>
    /// <exception cref="TimeoutException">
    /// The program was still running at the timeout, or its output had not ended, as a child it left behind holds it
    /// open; both are ended.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative, other than infinite, or too long.</exception>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The command ended with an exit code other than 0, which is the exception's error code; its error output, with the
    /// command line, is the message.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The program cannot be started.</exception>
    public static string ExecuteCommand(string fileName, string command, TimeSpan timeout)
    {
        return Execute(CreateStartInfo(fileName, command), $"{fileName} {command}", timeout);
    }

    /// <summary>Runs a program with its arguments, and waits for it asynchronously.</summary>
    /// <param name="fileName">The program.</param>
    /// <param name="command">The arguments, as one string.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the program's standard output.</returns>
    /// <remarks>A cancellation stops the wait, not the program.</remarks>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The command ended with an exit code other than 0, which is the exception's error code; its error output, with the
    /// command line, is the message.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The program cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<string> ExecuteCommandAsync(string fileName, string command,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(CreateStartInfo(fileName, command), $"{fileName} {command}", cancellationToken);
    }

    /// <summary>Runs a command line in bash, given to it as one argument, as written, and waits for it.</summary>
    /// <param name="command">The command line.</param>
    /// <returns>The command's standard output.</returns>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The command ended with an exit code other than 0, which is the exception's error code; its error output, with the
    /// command line, is the message.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The program cannot be started.</exception>
    // Never through Arguments, which parses its quotes again by Windows' rules: an inner double quote could change the
    // command bash runs
    public static string ExecuteShellCommand(string command)
    {
        return Execute(CreateShellStartInfo(command), $"/bin/bash -c {command}", Timeout.InfiniteTimeSpan);
    }

    /// <summary>Runs a command line in bash, as <see cref="ExecuteShellCommand"/> does, and waits for it asynchronously.</summary>
    /// <param name="command">The command line.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the command's standard output.</returns>
    /// <remarks>A cancellation stops the wait, not the program.</remarks>
    /// <exception cref="System.Runtime.InteropServices.ExternalException">
    /// The command ended with an exit code other than 0, which is the exception's error code; its error output, with the
    /// command line, is the message.
    /// </exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The program cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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
        var stopwatch = Stopwatch.StartNew();
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();

        // The timeout is for the command and its output together: a pipe stays open as long as any process holds
        // it, so a child the command leaves behind keeps the read waiting after the command itself has ended.
        var isFinished = process.WaitForExit(timeout);
        if (isFinished && timeout != Timeout.InfiniteTimeSpan) {
            var remaining = timeout - stopwatch.Elapsed;
            isFinished = Task.WaitAll([errorTask, outputTask], remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }

        if (!isFinished) {
            // its children too, which hold the pipes; one that cannot be ended must not hide the timeout
            VhInvoker.TryInvoke("end a command that timed out", LogLevel.Warning,
                () => process.Kill(entireProcessTree: true));
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
        // a cancel ends the wait for a command, not the command, so none starts after one
        cancellationToken.ThrowIfCancellationRequested();
        VhLogger.Instance.LogDebug($"Executing: {commandLine}");
        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        // both streams at once, as in Execute
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await errorTask.Vhc();
        var output = await outputTask.Vhc();

        await WaitForExitAsync(process, cancellationToken).Vhc();
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
        await tcs.Task.WaitAsync(cancellationToken).Vhc();
    }
}