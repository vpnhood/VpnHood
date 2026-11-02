using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.Devices.Linux;

// The service's side of the VPN host (LinuxVpnHost): its process, started at each connect, one at a time. The
// unit's cgroup ends it with the service: a stop's SIGTERM reaches both, and a crash's restart stops what is left.
internal sealed class LinuxVpnHostProcess(string executablePath, string storageFolder) : IDisposable
{
    private readonly AsyncLock _startLock = new();
    private Process? _process;
    private Task _processExit = Task.CompletedTask; // the process's exit, once told
    private int _endedProcessId; // the one this side ended, whose exit is no news
    private bool _disposed;

    public async Task Start(CancellationToken cancellationToken)
    {
        using var startLock = await _startLock.LockAsync(cancellationToken).Vhc();
        ObjectDisposedException.ThrowIf(_disposed, this);

        // One at a time: the last one, its session over, may be in its last seconds still, and its end would delete
        // the new one's channel file (TcpMessageListener) and could write over its state.
        if (_process is { } lastProcess) {
            if (!lastProcess.HasExited) {
                Volatile.Write(ref _endedProcessId, lastProcess.Id);
                lastProcess.Kill();
            }

            await _processExit.WaitAsync(cancellationToken).Vhc();
            lastProcess.Dispose();
            _process = null;
        }

        var startInfo = new ProcessStartInfo(executablePath, [LinuxVpnHost.StorageOption, storageFolder]) {
            UseShellExecute = false
        };
        var process = Process.Start(startInfo) ??
                      throw new InvalidOperationException($"Could not start the VPN host. Path: {executablePath}");

        VhLogger.Instance.LogInformation("The VPN host has started. ProcessId: {ProcessId}", process.Id);
        _process = process;
        _processExit = LogExit(process);
    }

    // A VPN host ends by itself with its session, exit code 0. Any other end is told here, as one that died before
    // its log opened tells nothing itself; the session's end is the app's (VpnServiceUnreachableException).
    private async Task LogExit(Process process)
    {
        await process.WaitForExitAsync(CancellationToken.None).Vhc();
        if (process.ExitCode == 0 || process.Id == Volatile.Read(ref _endedProcessId))
            VhLogger.Instance.LogDebug("The VPN host has exited. ProcessId: {ProcessId}, ExitCode: {ExitCode}",
                process.Id, process.ExitCode);
        else
            VhLogger.Instance.LogError("The VPN host has ended without its session. ProcessId: {ProcessId}, ExitCode: {ExitCode}",
                process.Id, process.ExitCode);
    }

    // The quick close: the VPN host ends at once. Its graceful end is its session's, which the app's disconnect asks
    // for before this.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (_process is not { } process || process.HasExited)
            return;

        Volatile.Write(ref _endedProcessId, process.Id);
        try {
            process.Kill();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not end the VPN host. ProcessId: {ProcessId}", process.Id);
        }
    }
}
